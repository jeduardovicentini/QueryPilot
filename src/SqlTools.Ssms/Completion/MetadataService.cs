using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SqlTools.Infra;
using SqlTools.Metadata;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Completion
{
    /// <summary>
    /// Cache de metadados. Objetos do usuário: por servidor + login + banco. Objetos de sistema (sys.*,
    /// INFORMATION_SCHEMA): uma vez por servidor + login, anexados a cada banco quando chegam.
    /// </summary>
    internal sealed class MetadataService
    {
        public static MetadataService Instance { get; } = new MetadataService();

        private readonly ConcurrentDictionary<string, Task<DatabaseMetadata>> _cache =
            new ConcurrentDictionary<string, Task<DatabaseMetadata>>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Task<List<DbObject>>> _system =
            new ConcurrentDictionary<string, Task<List<DbObject>>>(StringComparer.OrdinalIgnoreCase);

        private static readonly Regex Ddl = new Regex(@"\b(CREATE|ALTER|DROP|sp_rename)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string KeyOf(ConnectionSnapshot c) => c.Key + "|" + (c.Database ?? "").ToUpperInvariant();

        /// <summary>
        /// Metadados do banco da conexão. Se ainda não carregou, espera até <paramref name="wait"/>; passando disso,
        /// devolve null (o carregamento continua e a próxima lista já vem completa).
        /// </summary>
        public async Task<DatabaseMetadata> GetAsync(ConnectionSnapshot conn, TimeSpan wait, CancellationToken token)
        {
            if (conn?.UIConnectionInfo == null || string.IsNullOrEmpty(conn.Server)) return null;
            var key = KeyOf(conn);
            var serverKey = conn.Key;
            Task<DatabaseMetadata> task;
            lock (_cache)
            {
                if (!_system.ContainsKey(serverKey))
                {
                    var sys = Task.Run(() => LoadSystem(conn));
                    _system[serverKey] = sys;
                    sys.ContinueWith(_ => AttachSystem(serverKey), TaskScheduler.Default);
                }
                if (!_cache.TryGetValue(key, out task))
                {
                    task = Task.Run(() => Load(conn));
                    _cache[key] = task;
                    task.ContinueWith(_ => AttachSystem(serverKey), TaskScheduler.Default);
                }
            }
            if (!task.IsCompleted)
            {
                var done = await Task.WhenAny(task, Task.Delay(wait, token)).ConfigureAwait(false);
                if (done != task) return null;
            }
            return Result(key, task);
        }

        private DatabaseMetadata Result(string key, Task<DatabaseMetadata> task)
        {
            if (task.Status == TaskStatus.RanToCompletion && task.Result != null) return task.Result;
            // falhou: esquece, para tentar de novo (depois de 30 s)
            if (task.IsCompleted && _cache.TryGetValue(key, out var cached) && cached == task)
                Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => _cache.TryRemove(key, out var _x), TaskScheduler.Default);
            return null;
        }

        /// <summary>Entrega os objetos de sistema do servidor a todos os bancos já carregados dele.</summary>
        private void AttachSystem(string serverKey)
        {
            if (!_system.TryGetValue(serverKey, out var sys)) return;
            if (sys.Status != TaskStatus.RanToCompletion || sys.Result == null)
            {
                if (sys.IsCompleted) Task.Delay(TimeSpan.FromSeconds(30)).ContinueWith(_ => _system.TryRemove(serverKey, out var _x), TaskScheduler.Default);
                return;
            }
            foreach (var kv in _cache.Where(kv => kv.Key.StartsWith(serverKey + "|", StringComparison.OrdinalIgnoreCase)))
            {
                var t = kv.Value;
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null && t.Result.SystemObjects.Count == 0)
                    t.Result.SystemObjects = sys.Result;
            }
        }

        public void Invalidate(ConnectionSnapshot conn)
        {
            if (conn == null) return;
            _cache.TryRemove(KeyOf(conn), out _);
        }

        public void InvalidateAll()
        {
            _cache.Clear();
            _system.Clear();
        }

        /// <summary>Depois de executar DDL (CREATE/ALTER/DROP), recarrega os metadados daquele servidor na próxima lista.</summary>
        public void OnExecuted(string server, string login, string database, string sql)
        {
            if (string.IsNullOrEmpty(sql) || !Ddl.IsMatch(sql)) return;
            var prefix = ConnectionSnapshot.MakeKey(server, login) + "|";
            foreach (var k in _cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
                _cache.TryRemove(k, out _);
        }

        private static DatabaseMetadata Load(ConnectionSnapshot conn)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return Query(conn, MetadataLoader.Query, (r, dbName) =>
                {
                    var md = MetadataLoader.Load(r, conn.Server, dbName);
                    Log.Info($"Metadados de {conn.Server}/{dbName}: {md.Objects.Count} objetos, {md.ForeignKeys.Count} FKs em {sw.ElapsedMilliseconds} ms.");
                    return md;
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao carregar metadados de {conn.Server}/{conn.Database}.", ex);
                return null;
            }
        }

        private static List<DbObject> LoadSystem(ConnectionSnapshot conn)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                return Query(conn, MetadataLoader.SystemQuery, (r, dbName) =>
                {
                    var list = MetadataLoader.LoadSystem(r);
                    Log.Info($"Objetos de sistema de {conn.Server}: {list.Count} em {sw.ElapsedMilliseconds} ms.");
                    return list;
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"Falha ao carregar objetos de sistema de {conn.Server}.", ex);
                return null;
            }
        }

        private static T Query<T>(ConnectionSnapshot conn, string sql, Func<IDataReader, string, T> read) where T : class
        {
            using (var db = SsmsBridge.CreateDbConnection(conn.UIConnectionInfo, conn.Database))
            {
                if (db == null) return null;
                db.Open();
                using (var cmd = db.CreateCommand())
                {
                    cmd.CommandText = sql;
                    cmd.CommandTimeout = 120;
                    using (var r = cmd.ExecuteReader()) return read(r, db.Database);
                }
            }
        }
    }
}
