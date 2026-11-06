using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Settings;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.History
{
    /// <summary>Grava no histórico cada execução feita no editor de query (F5 / Execute / Ctrl+E).</summary>
    internal sealed class ExecutionMonitor
    {
        // Query.Execute do SSMS
        private const string ExecuteCmdGuid = "{52692960-56BC-4989-B5D3-94C47A513E8D}";
        private const int ExecuteCmdId = 1;

        private readonly DTE _dte;
        private readonly HistoryStore _store;
        private readonly SettingsStore _settings;
        private CommandEvents _executeEvents; // manter referência: senão o COM solta o evento

        private readonly ConditionalWeakTable<object, Pending> _pending = new ConditionalWeakTable<object, Pending>();
        private readonly ConditionalWeakTable<object, object> _hooked = new ConditionalWeakTable<object, object>();

        private sealed class Pending
        {
            public long Id;
            public Stopwatch Clock;
            public object Editor;
            public string Database;
        }

        public event EventHandler<HistoryEntry> ExecutionRecorded;

        public ExecutionMonitor(DTE dte, HistoryStore store, SettingsStore settings)
        {
            _dte = dte;
            _store = store;
            _settings = settings;
        }

        public void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _executeEvents = _dte.Events.get_CommandEvents(ExecuteCmdGuid, ExecuteCmdId);
            _executeEvents.BeforeExecute += OnBeforeExecute;
            Log.Info("Monitor de execução ativo.");
        }

        private void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (!_settings.Current.CaptureEnabled) return;

                var frame = SsmsBridge.GetActiveDocumentFrame();
                var editor = SsmsBridge.GetEditorControl(frame);
                if (editor == null) return;

                var text = GetTextToExecute();
                if (string.IsNullOrWhiteSpace(text)) return;

                var conn = SsmsBridge.GetEditorConnection(editor) ?? SsmsBridge.GetActiveWindowConnection() ?? new ConnectionSnapshot();
                var entry = new HistoryEntry
                {
                    ExecutedAtUtc = DateTime.UtcNow,
                    Server = conn.Server,
                    Database = conn.Database,
                    Login = conn.Login,
                    Status = ExecutionStatus.Running,
                    QueryText = text,
                };
                _store.Add(entry);

                var results = SsmsBridge.GetResultsControl(editor);
                if (results != null && EnsureHooked(results))
                {
                    _pending.Remove(results);
                    _pending.Add(results, new Pending { Id = entry.Id, Clock = Stopwatch.StartNew(), Editor = editor, Database = entry.Database });
                }
                ExecutionRecorded?.Invoke(this, entry);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao registrar execução no histórico.", ex);
            }
        }

        private bool EnsureHooked(object results)
        {
            if (_hooked.TryGetValue(results, out _)) return true;
            if (!SsmsBridge.HookExecutionCompleted(results, OnExecutionCompleted)) return false;
            _hooked.Add(results, new object());
            return true;
        }

        private void OnExecutionCompleted(object results, string result)
        {
            if (results == null || !_pending.TryGetValue(results, out var p)) return;
            _pending.Remove(results);
            p.Clock.Stop();

            var status = MapStatus(result);
            var rows = SsmsBridge.GetRowsAffected(results);
            string db = null;
            if (string.IsNullOrEmpty(p.Database))
            {
                try { db = SsmsBridge.GetEditorConnection(p.Editor)?.Database; }
                catch { }
            }
            try
            {
                _store.Complete(p.Id, status, p.Clock.ElapsedMilliseconds, rows, db);
                var entry = _store.Get(p.Id);
                if (entry != null) ExecutionRecorded?.Invoke(this, entry);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao gravar fim da execução " + p.Id, ex);
            }
        }

        internal static ExecutionStatus MapStatus(string result)
        {
            if (string.IsNullOrEmpty(result)) return ExecutionStatus.Success;
            if (result.IndexOf("Cancel", StringComparison.OrdinalIgnoreCase) >= 0) return ExecutionStatus.Cancelled;
            if (result.IndexOf("Failure", StringComparison.OrdinalIgnoreCase) >= 0 ||
                result.IndexOf("Timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                result.IndexOf("Halted", StringComparison.OrdinalIgnoreCase) >= 0) return ExecutionStatus.Error;
            return ExecutionStatus.Success;
        }

        /// <summary>Texto selecionado, ou o documento inteiro se não houver seleção (mesma regra do SSMS).</summary>
        private string GetTextToExecute()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(_dte.ActiveDocument?.Object("TextDocument") is TextDocument doc)) return null;
            var sel = doc.Selection;
            if (sel != null && !sel.IsEmpty && !string.IsNullOrWhiteSpace(sel.Text)) return sel.Text;
            return doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
        }
    }
}
