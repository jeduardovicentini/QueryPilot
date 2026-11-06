using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace SqlTools.History
{
    /// <summary>
    /// Histórico de execuções em SQLite. Thread-safe: cada operação abre a própria conexão (pool do provider).
    /// </summary>
    public sealed class HistoryStore
    {
        public const int MaxQueryLength = 500_000;
        private readonly string _connectionString;

        public HistoryStore(string dbPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath)));
            _connectionString = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                JournalMode = SQLiteJournalModeEnum.Wal,
                BusyTimeout = 5000,
                Pooling = true,
                FailIfMissing = false,
            }.ToString();
            EnsureSchema();
        }

        private SQLiteConnection Open()
        {
            var c = new SQLiteConnection(_connectionString);
            c.Open();
            return c;
        }

        private void EnsureSchema()
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS QueryHistory (
    Id            INTEGER PRIMARY KEY AUTOINCREMENT,
    ExecutedAt    INTEGER NOT NULL,          -- ticks UTC
    Server        TEXT    NOT NULL DEFAULT '',
    DatabaseName  TEXT    NOT NULL DEFAULT '',
    Login         TEXT    NOT NULL DEFAULT '',
    DurationMs    INTEGER NULL,
    RowsAffected  INTEGER NULL,
    Status        INTEGER NOT NULL DEFAULT 0,
    QueryText     TEXT    NOT NULL
);
CREATE INDEX IF NOT EXISTS IX_QueryHistory_ExecutedAt ON QueryHistory (ExecutedAt DESC);
CREATE INDEX IF NOT EXISTS IX_QueryHistory_Server ON QueryHistory (Server, DatabaseName);
PRAGMA user_version = 1;";
                cmd.ExecuteNonQuery();
            }
        }

        public long Add(HistoryEntry e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            var text = e.QueryText ?? string.Empty;
            if (text.Length > MaxQueryLength) text = text.Substring(0, MaxQueryLength);

            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"INSERT INTO QueryHistory (ExecutedAt, Server, DatabaseName, Login, DurationMs, RowsAffected, Status, QueryText)
VALUES (@at, @srv, @db, @login, @dur, @rows, @st, @txt); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@at", ToTicks(e.ExecutedAtUtc));
                cmd.Parameters.AddWithValue("@srv", e.Server ?? string.Empty);
                cmd.Parameters.AddWithValue("@db", e.Database ?? string.Empty);
                cmd.Parameters.AddWithValue("@login", e.Login ?? string.Empty);
                cmd.Parameters.AddWithValue("@dur", (object)e.DurationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rows", (object)e.RowsAffected ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@st", (int)e.Status);
                cmd.Parameters.AddWithValue("@txt", text);
                e.Id = Convert.ToInt64(cmd.ExecuteScalar());
                return e.Id;
            }
        }

        /// <summary>Guarda o texto de uma aba fechada; a mesma query no mesmo servidor/banco fica só uma vez (a mais recente).</summary>
        public long AddClosedTab(string server, string database, string login, string text, DateTime closedAtUtc)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM QueryHistory WHERE Status = @st AND Server = @srv AND DatabaseName = @db AND QueryText = @txt";
                cmd.Parameters.AddWithValue("@st", (int)ExecutionStatus.ClosedTab);
                cmd.Parameters.AddWithValue("@srv", server ?? string.Empty);
                cmd.Parameters.AddWithValue("@db", database ?? string.Empty);
                cmd.Parameters.AddWithValue("@txt", text.Length > MaxQueryLength ? text.Substring(0, MaxQueryLength) : text);
                cmd.ExecuteNonQuery();
            }
            return Add(new HistoryEntry
            {
                ExecutedAtUtc = closedAtUtc,
                Server = server,
                Database = database,
                Login = login,
                Status = ExecutionStatus.ClosedTab,
                QueryText = text,
            });
        }

        public void Complete(long id, ExecutionStatus status, long? durationMs, long? rowsAffected, string database = null)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = @"UPDATE QueryHistory SET Status = @st, DurationMs = @dur, RowsAffected = @rows,
DatabaseName = CASE WHEN DatabaseName = '' AND @db IS NOT NULL THEN @db ELSE DatabaseName END
WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@st", (int)status);
                cmd.Parameters.AddWithValue("@dur", (object)durationMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@rows", (object)rowsAffected ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@db", (object)database ?? DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }

        public HistoryEntry Get(long id)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT * FROM QueryHistory WHERE Id = @id";
                cmd.Parameters.AddWithValue("@id", id);
                using (var r = cmd.ExecuteReader())
                    return r.Read() ? Map(r) : null;
            }
        }

        public List<HistoryEntry> Search(HistoryQuery q)
        {
            q = q ?? new HistoryQuery();
            var sql = new StringBuilder("SELECT * FROM QueryHistory WHERE Status " + (q.ClosedTabs ? "= " : "<> ") + (int)ExecutionStatus.ClosedTab);
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                if (!string.IsNullOrWhiteSpace(q.Text))
                {
                    sql.Append(" AND QueryText LIKE @txt ESCAPE '\\'");
                    cmd.Parameters.AddWithValue("@txt", "%" + EscapeLike(q.Text.Trim()) + "%");
                }
                if (q.FromUtc.HasValue)
                {
                    sql.Append(" AND ExecutedAt >= @from");
                    cmd.Parameters.AddWithValue("@from", ToTicks(q.FromUtc.Value));
                }
                if (q.ToUtc.HasValue)
                {
                    sql.Append(" AND ExecutedAt < @to");
                    cmd.Parameters.AddWithValue("@to", ToTicks(q.ToUtc.Value));
                }
                if (q.Servers != null && q.Servers.Count > 0)
                {
                    var names = new List<string>();
                    for (int i = 0; i < q.Servers.Count; i++)
                    {
                        names.Add("@s" + i);
                        cmd.Parameters.AddWithValue("@s" + i, q.Servers[i] ?? string.Empty);
                    }
                    sql.Append(" AND Server IN (").Append(string.Join(",", names)).Append(")");
                }
                if (!string.IsNullOrEmpty(q.Database))
                {
                    sql.Append(" AND DatabaseName = @db");
                    cmd.Parameters.AddWithValue("@db", q.Database);
                }
                if (q.OnlyErrors)
                {
                    sql.Append(" AND Status = @err");
                    cmd.Parameters.AddWithValue("@err", (int)ExecutionStatus.Error);
                }
                sql.Append(" ORDER BY ExecutedAt DESC, Id DESC LIMIT @limit");
                cmd.Parameters.AddWithValue("@limit", q.Limit <= 0 ? 2000 : q.Limit);
                cmd.CommandText = sql.ToString();

                var list = new List<HistoryEntry>();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(Map(r));
                return list;
            }
        }

        public List<string> GetServers() => Distinct("SELECT DISTINCT Server FROM QueryHistory WHERE Server <> '' ORDER BY Server COLLATE NOCASE", null);

        public List<string> GetDatabases(string server = null) => server == null
            ? Distinct("SELECT DISTINCT DatabaseName FROM QueryHistory WHERE DatabaseName <> '' ORDER BY DatabaseName COLLATE NOCASE", null)
            : Distinct("SELECT DISTINCT DatabaseName FROM QueryHistory WHERE DatabaseName <> '' AND Server = @p ORDER BY DatabaseName COLLATE NOCASE", server);

        /// <summary>Apaga execuções mais antigas que <paramref name="olderThanUtc"/>. Retorna quantas apagou.</summary>
        public int Purge(DateTime olderThanUtc)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "DELETE FROM QueryHistory WHERE ExecutedAt < @t";
                cmd.Parameters.AddWithValue("@t", ToTicks(olderThanUtc));
                return cmd.ExecuteNonQuery();
            }
        }

        public long Count()
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT COUNT(*) FROM QueryHistory";
                return Convert.ToInt64(cmd.ExecuteScalar());
            }
        }

        private List<string> Distinct(string sql, string p)
        {
            using (var c = Open())
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = sql;
                if (p != null) cmd.Parameters.AddWithValue("@p", p);
                var list = new List<string>();
                using (var r = cmd.ExecuteReader())
                    while (r.Read()) list.Add(r.GetString(0));
                return list;
            }
        }

        private static HistoryEntry Map(IDataRecord r) => new HistoryEntry
        {
            Id = r.GetInt64(r.GetOrdinal("Id")),
            ExecutedAtUtc = new DateTime(r.GetInt64(r.GetOrdinal("ExecutedAt")), DateTimeKind.Utc),
            Server = r["Server"] as string ?? string.Empty,
            Database = r["DatabaseName"] as string ?? string.Empty,
            Login = r["Login"] as string ?? string.Empty,
            DurationMs = r["DurationMs"] is DBNull ? (long?)null : Convert.ToInt64(r["DurationMs"]),
            RowsAffected = r["RowsAffected"] is DBNull ? (long?)null : Convert.ToInt64(r["RowsAffected"]),
            Status = (ExecutionStatus)Convert.ToInt32(r["Status"]),
            QueryText = r["QueryText"] as string ?? string.Empty,
        };

        private static long ToTicks(DateTime dt) =>
            (dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt).Ticks;

        internal static string EscapeLike(string s) =>
            s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }
}
