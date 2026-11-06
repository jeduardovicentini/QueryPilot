using System;
using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlTools.Infra;

namespace SqlTools.Ssms.Integration
{
    /// <summary>Conexão de uma janela de query, no momento da leitura.</summary>
    internal sealed class ConnectionSnapshot
    {
        public string Server { get; set; } = "";
        public string Database { get; set; } = "";
        public string Login { get; set; } = "";
        public int AuthenticationType { get; set; }
        /// <summary>Objeto UIConnectionInfo do SSMS (vivo). Usado para reabrir queries com a mesma conexão.</summary>
        public object UIConnectionInfo { get; set; }

        public string Key => MakeKey(Server, Login);
        public static string MakeKey(string server, string login) => (server ?? "").ToUpperInvariant() + "|" + (login ?? "").ToUpperInvariant();
    }

    /// <summary>
    /// Único ponto que toca internals do SSMS 22 (via reflection, para sobreviver a atualizações).
    /// Se algo mudar numa versão futura, o efeito é "recurso indisponível" + log, nunca exceção no SSMS.
    /// </summary>
    internal static class SsmsBridge
    {
        private const string SqlEditorTypeName = "SqlScriptEditorControl";
        private static readonly Guid SqlServerType = new Guid("8c91a03d-f9b4-46c0-a305-b5dcc79ff907");
        private static readonly ConcurrentDictionary<string, bool> Warned = new ConcurrentDictionary<string, bool>();

        /// <summary>Conexões vistas nesta sessão do SSMS, para reabrir queries sem pedir senha de novo.</summary>
        private static readonly ConcurrentDictionary<string, object> SessionConnections = new ConcurrentDictionary<string, object>();

        private static Type _serviceCacheType;

        private static Type ServiceCacheType =>
            _serviceCacheType ?? (_serviceCacheType = Reflect.FindType("Microsoft.SqlServer.Management.UI.VSIntegration.ServiceCache", "SqlPackageBase"));

        public static object ScriptFactory => Reflect.GetStatic(ServiceCacheType, "ScriptFactory");

        public static void WarnOnce(string key, string message, Exception ex = null)
        {
            if (Warned.TryAdd(key, true)) Log.Warn(message, ex);
        }

        // ---------- Editor / conexão ----------

        public static bool IsSqlEditor(object o)
        {
            for (var t = o?.GetType(); t != null; t = t.BaseType)
                if (t.Name == SqlEditorTypeName) return true;
            return false;
        }

        public static object GetEditorControl(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (frame == null) return null;
            try
            {
                frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out var view);
                if (IsSqlEditor(view)) return view;
            }
            catch (Exception ex)
            {
                WarnOnce("docview", "Não foi possível ler o DocView da janela.", ex);
            }
            return null;
        }

        public static IVsWindowFrame GetActiveDocumentFrame()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection sel &&
                sel.GetCurrentElementValue((uint)VSConstants.VSSELELEMID.SEID_DocumentFrame, out var frame) == 0)
                return frame as IVsWindowFrame;
            return null;
        }

        public static object GetResultsControl(object editor) => Reflect.Get(editor, "m_sqlResultsControl");

        private static object GetExec(object editor) => Reflect.Get(GetResultsControl(editor), "m_sqlExec");

        /// <summary>Conexão da janela de query (servidor, login, banco atual).</summary>
        public static ConnectionSnapshot GetEditorConnection(object editor)
        {
            // ScriptAndResultsEditorControl.Connection / m_connection; fallback no executor (só existe após executar)
            var exec = GetExec(editor);
            var ui = Reflect.Get(editor, "Connection") ?? Reflect.Get(exec, "CurrentUIConnectionInfo");
            if (ui == null) return null;

            var snap = FromUIConnectionInfo(ui);
            if ((Reflect.Get(editor, "m_connection") ?? Reflect.Get(exec, "CurrentConn")) is IDbConnection conn)
            {
                try
                {
                    if (conn.State == ConnectionState.Open && !string.IsNullOrEmpty(conn.Database)) snap.Database = conn.Database;
                }
                catch
                {
                    // conexão sendo fechada
                }
            }
            Remember(snap);
            return snap;
        }

        /// <summary>Conexão da janela ativa segundo o ScriptFactory (fallback).</summary>
        public static ConnectionSnapshot GetActiveWindowConnection()
        {
            var info = Reflect.Get(ScriptFactory, "CurrentlyActiveWndConnectionInfo");
            var ui = Reflect.Get(info, "UIConnectionInfo");
            if (ui == null) return null;
            var snap = FromUIConnectionInfo(ui);
            Remember(snap);
            return snap;
        }

        private static ConnectionSnapshot FromUIConnectionInfo(object ui)
        {
            var snap = new ConnectionSnapshot
            {
                Server = Reflect.Get(ui, "ServerName") as string ?? "",
                Login = Reflect.Get(ui, "UserName") as string ?? "",
                AuthenticationType = Reflect.Get(ui, "AuthenticationType") is int a ? a : 0,
                UIConnectionInfo = ui,
            };
            if (snap.AuthenticationType == 0 && string.IsNullOrEmpty(snap.Login))
                snap.Login = Environment.UserDomainName + "\\" + Environment.UserName;
            if (Reflect.Get(ui, "AdvancedOptions") is NameValueCollection adv)
                snap.Database = adv["DATABASE"] ?? "";
            return snap;
        }

        private static void Remember(ConnectionSnapshot snap)
        {
            if (snap?.UIConnectionInfo != null && !string.IsNullOrEmpty(snap.Server))
                SessionConnections[snap.Key] = snap.UIConnectionInfo;
        }

        public static long? GetRowsAffected(object resultsControl)
        {
            var v = Reflect.Get(resultsControl, "RowsAffected");
            return v is long l && l >= 0 ? l : (long?)null;
        }

        /// <summary>
        /// Assina o fim da execução do controle de resultados (evento interno ScriptExecutionCompleted).
        /// O callback recebe o nome do resultado ("Success", "Failure", "Cancel"...).
        /// </summary>
        public static bool HookExecutionCompleted(object resultsControl, Action<object, string> onCompleted)
        {
            if (resultsControl == null) return false;
            try
            {
                var ev = Reflect.FindEvent(resultsControl.GetType(), "ScriptExecutionCompleted");
                if (ev == null)
                {
                    WarnOnce("evt", "Evento ScriptExecutionCompleted não encontrado em " + resultsControl.GetType().FullName);
                    return false;
                }
                var relay = new CompletionRelay(resultsControl, onCompleted);
                var d = Delegate.CreateDelegate(ev.EventHandlerType, relay, typeof(CompletionRelay).GetMethod(nameof(CompletionRelay.Handle)));
                ev.GetAddMethod(true).Invoke(resultsControl, new object[] { d });
                return true;
            }
            catch (Exception ex)
            {
                WarnOnce("hook", "Falha ao assinar ScriptExecutionCompleted.", ex);
                return false;
            }
        }

        private sealed class CompletionRelay
        {
            private static bool _loggedFirst;
            private readonly object _resultsControl;
            private readonly Action<object, string> _callback;

            public CompletionRelay(object resultsControl, Action<object, string> callback)
            {
                _resultsControl = resultsControl;
                _callback = callback;
            }

            // (object, object) liga por contravariância ao delegate interno (object, ScriptExecutionCompletedEventArgs).
            // O sender nem sempre é o controle de resultados, por isso usamos o controle capturado.
            public void Handle(object sender, object e)
            {
                try
                {
                    var result = Reflect.Get(e, "ExecutionResult")?.ToString() ?? "";
                    if (!_loggedFirst)
                    {
                        _loggedFirst = true;
                        Log.Info($"Primeiro ScriptExecutionCompleted: sender={sender?.GetType().Name}, result={result}");
                    }
                    _callback(_resultsControl, result);
                }
                catch (Exception ex)
                {
                    Log.Error("Erro ao processar fim de execução.", ex);
                }
            }
        }

        // ---------- Abrir query ----------

        /// <summary>Monta um UIConnectionInfo para reabrir uma query: reaproveita a conexão da sessão ou cria uma Windows Auth.</summary>
        public static object BuildConnection(string server, string login, string database)
        {
            if (string.IsNullOrEmpty(server)) return null;
            object ui = null;
            if (SessionConnections.TryGetValue(ConnectionSnapshot.MakeKey(server, login), out var live))
            {
                ui = SafeCopy(live);
            }
            else
            {
                var match = SessionConnections.FirstOrDefault(kv => kv.Key.StartsWith(server.ToUpperInvariant() + "|", StringComparison.Ordinal));
                if (match.Value != null && IsWindowsLogin(login)) ui = SafeCopy(match.Value);
            }

            if (ui == null && IsWindowsLogin(login))
            {
                var type = Reflect.FindType("Microsoft.SqlServer.Management.Smo.RegSvrEnum.UIConnectionInfo", "Microsoft.SqlServer.RegSvrEnum");
                if (type == null) return null;
                ui = Activator.CreateInstance(type);
                Set(ui, "ServerName", server);
                Set(ui, "ServerType", SqlServerType);
                Set(ui, "AuthenticationType", 0);
            }
            if (ui == null) return null;

            if (!string.IsNullOrEmpty(database) && Reflect.Get(ui, "AdvancedOptions") is NameValueCollection adv)
                adv["DATABASE"] = database;
            return ui;
        }

        private static bool IsWindowsLogin(string login) => string.IsNullOrEmpty(login) || login.Contains("\\");

        private static object SafeCopy(object ui)
        {
            try { return Reflect.Invoke(ui, "Copy"); }
            catch { return ui; }
        }

        private static void Set(object target, string prop, object value)
        {
            var p = target.GetType().GetProperty(prop, BindingFlags.Instance | BindingFlags.Public);
            p?.SetValue(target, value, null);
        }

        /// <summary>Abre uma nova janela de query (conectada se <paramref name="uiConnection"/> != null).</summary>
        public static bool OpenNewQueryWindow(object uiConnection)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var sf = ScriptFactory;
            if (sf == null) return false;

            var scriptTypeEnum = Reflect.FindType("Microsoft.SqlServer.Management.UI.VSIntegration.Editors.ScriptType", "SqlWorkbench.Interfaces");
            if (scriptTypeEnum == null) return false;
            var sql = Enum.Parse(scriptTypeEnum, "Sql");

            var methods = sf.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public).Where(m => m.Name == "CreateNewBlankScript").ToList();
            if (uiConnection != null)
            {
                var m = methods.FirstOrDefault(x =>
                {
                    var ps = x.GetParameters();
                    return ps.Length == 3 && ps[1].ParameterType.IsInstanceOfType(uiConnection);
                });
                if (m != null)
                {
                    m.Invoke(sf, new[] { sql, uiConnection, null });
                    return true;
                }
            }
            var m1 = methods.FirstOrDefault(x => x.GetParameters().Length == 1);
            if (m1 == null) return false;
            m1.Invoke(sf, new[] { sql });
            return true;
        }
    }
}
