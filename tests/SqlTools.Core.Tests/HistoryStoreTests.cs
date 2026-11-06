using System;
using System.IO;
using System.Linq;
using SqlTools.History;
using Xunit;

namespace SqlTools.Core.Tests
{
    public sealed class HistoryStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "sqltools-tests-" + Guid.NewGuid().ToString("N"));
        private readonly HistoryStore _store;

        public HistoryStoreTests()
        {
            _store = new HistoryStore(Path.Combine(_dir, "h.db"));
        }

        public void Dispose()
        {
            System.Data.SQLite.SQLiteConnection.ClearAllPools();
            try { Directory.Delete(_dir, true); } catch { }
        }

        private long Add(string sql, string server = "PROD", string db = "Vendas", DateTime? at = null, ExecutionStatus st = ExecutionStatus.Success)
            => _store.Add(new HistoryEntry
            {
                ExecutedAtUtc = at ?? DateTime.UtcNow,
                Server = server,
                Database = db,
                Login = "eduardo",
                Status = st,
                QueryText = sql,
            });

        [Fact]
        public void Add_then_Get_roundtrips_all_fields()
        {
            var at = new DateTime(2026, 11, 6, 14, 32, 0, DateTimeKind.Utc);
            var id = _store.Add(new HistoryEntry
            {
                ExecutedAtUtc = at, Server = "PROD-SQL01", Database = "Vendas", Login = "dom\\eduardo",
                DurationMs = 1234, RowsAffected = 42, Status = ExecutionStatus.Success, QueryText = "SELECT 1",
            });

            var e = _store.Get(id);
            Assert.Equal(at, e.ExecutedAtUtc);
            Assert.Equal("PROD-SQL01", e.Server);
            Assert.Equal("Vendas", e.Database);
            Assert.Equal("dom\\eduardo", e.Login);
            Assert.Equal(1234, e.DurationMs);
            Assert.Equal(42, e.RowsAffected);
            Assert.Equal(ExecutionStatus.Success, e.Status);
            Assert.Equal("SELECT 1", e.QueryText);
        }

        [Fact]
        public void Complete_updates_status_duration_rows_and_fills_missing_database()
        {
            var id = Add("SELECT 1", db: "", st: ExecutionStatus.Running);
            _store.Complete(id, ExecutionStatus.Error, 50, 0, "master");

            var e = _store.Get(id);
            Assert.Equal(ExecutionStatus.Error, e.Status);
            Assert.Equal(50, e.DurationMs);
            Assert.Equal(0, e.RowsAffected);
            Assert.Equal("master", e.Database);
        }

        [Fact]
        public void Complete_does_not_overwrite_known_database()
        {
            var id = Add("SELECT 1", db: "Vendas", st: ExecutionStatus.Running);
            _store.Complete(id, ExecutionStatus.Success, 1, 1, "master");
            Assert.Equal("Vendas", _store.Get(id).Database);
        }

        [Fact]
        public void Search_by_text_is_case_insensitive_and_treats_wildcards_literally()
        {
            Add("SELECT * FROM Clientes");
            Add("UPDATE Pedidos SET x = 1");
            Add("SELECT 100% FROM t_a");

            Assert.Single(_store.Search(new HistoryQuery { Text = "clientes" }));
            Assert.Single(_store.Search(new HistoryQuery { Text = "100%" }));
            Assert.Single(_store.Search(new HistoryQuery { Text = "t_a" }));
            Assert.Empty(_store.Search(new HistoryQuery { Text = "t%a" }));
        }

        [Fact]
        public void Search_filters_by_period_server_database_and_errors()
        {
            var now = DateTime.UtcNow;
            Add("a", "PROD", "Vendas", now.AddDays(-10));
            Add("b", "PROD", "RH", now.AddHours(-1), ExecutionStatus.Error);
            Add("c", "DEV", "Vendas", now);

            Assert.Equal(new[] { "c", "b" }, _store.Search(new HistoryQuery { FromUtc = now.AddDays(-7) }).Select(e => e.QueryText));
            Assert.Equal(new[] { "b", "a" }, _store.Search(new HistoryQuery { Servers = new[] { "PROD" } }).Select(e => e.QueryText));
            Assert.Equal(new[] { "c", "a" }, _store.Search(new HistoryQuery { Database = "Vendas" }).Select(e => e.QueryText));
            Assert.Equal(new[] { "b" }, _store.Search(new HistoryQuery { OnlyErrors = true }).Select(e => e.QueryText));
        }

        [Fact]
        public void Search_orders_newest_first_and_respects_limit()
        {
            var now = DateTime.UtcNow;
            for (int i = 0; i < 5; i++) Add("q" + i, at: now.AddMinutes(i));
            var r = _store.Search(new HistoryQuery { Limit = 2 });
            Assert.Equal(new[] { "q4", "q3" }, r.Select(e => e.QueryText));
        }

        [Fact]
        public void Servers_and_databases_are_distinct_and_sorted()
        {
            Add("a", "zeta", "B");
            Add("b", "Alpha", "A");
            Add("c", "Alpha", "A");
            Assert.Equal(new[] { "Alpha", "zeta" }, _store.GetServers());
            Assert.Equal(new[] { "A" }, _store.GetDatabases("Alpha"));
            Assert.Equal(new[] { "A", "B" }, _store.GetDatabases());
        }

        [Fact]
        public void Purge_removes_only_old_entries()
        {
            var now = DateTime.UtcNow;
            Add("old", at: now.AddDays(-100));
            Add("new", at: now);
            Assert.Equal(1, _store.Purge(now.AddDays(-90)));
            Assert.Equal(1, _store.Count());
        }

        [Fact]
        public void Very_long_query_is_truncated()
        {
            var id = Add(new string('x', HistoryStore.MaxQueryLength + 10));
            Assert.Equal(HistoryStore.MaxQueryLength, _store.Get(id).QueryText.Length);
        }
    }
}
