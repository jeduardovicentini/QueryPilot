using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Safety;
using SqlTools.Settings;
using SqlTools.Ssms.Safety;
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
            public bool TestMode;
        }

        public event EventHandler<HistoryEntry> ExecutionRecorded;

        /// <summary>Pergunta se executa mesmo com DELETE/UPDATE sem WHERE. Trocável no self-test.</summary>
        internal Func<IReadOnlyList<UnsafeStatement>, bool> ConfirmUnsafe { get; set; } = AskUser;

        /// <summary>Quantas execuções o alerta cancelou (self-test).</summary>
        internal int UnsafeCancelled { get; private set; }

        /// <summary>Aviso do modo de teste (execução recusada, cancelada no meio). Trocável no self-test.</summary>
        internal Action<string> Warn { get; set; } = msg =>
            System.Windows.MessageBox.Show(msg, ProductInfo.Name + " — modo de teste", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);

        /// <summary>Última mensagem do modo de teste (self-test).</summary>
        internal string LastTestModeWarning { get; private set; }

        private static bool AskUser(IReadOnlyList<UnsafeStatement> issues)
        {
            var list = string.Join(Environment.NewLine, issues.Take(8).Select(i => "   • " + i));
            if (issues.Count > 8) list += Environment.NewLine + "   • ... e mais " + (issues.Count - 8);
            var msg = "Esta execução altera tabelas inteiras (sem WHERE):" + Environment.NewLine + Environment.NewLine + list +
                      Environment.NewLine + Environment.NewLine + "Executar mesmo assim?";
            return System.Windows.MessageBox.Show(msg, ProductInfo.Name + " — confirmação", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
        }

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
            // o SSMS já leu o texto: tira o BEGIN TRAN/ROLLBACK do modo de teste
            _executeEvents.AfterExecute += (g, i, a, b) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                TestModeController.Restore();
            };
            Log.Info("Monitor de execução ativo.");
        }

        private void OnBeforeExecute(string guid, int id, object customIn, object customOut, ref bool cancelDefault)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var testMode = false;
            try
            {
                var settings = _settings.Current;
                var view = EditorAccess.GetActiveWpfTextView();
                testMode = view != null && TestModeController.IsOn(view.TextBuffer);
                if (!settings.CaptureEnabled && !settings.UnsafeGuardEnabled && !testMode) return;

                var frame = SsmsBridge.GetActiveDocumentFrame();
                var editor = SsmsBridge.GetEditorControl(frame);
                if (editor == null) return;

                var text = GetTextToExecute();
                if (string.IsNullOrWhiteSpace(text)) return;

                if (testMode)
                {
                    // no modo de teste tudo é desfeito: não precisa do alerta de DELETE/UPDATE sem WHERE
                    var error = TestModeController.Prepare(view);
                    if (error != null)
                    {
                        cancelDefault = true;
                        WarnTestMode(error);
                        return;
                    }
                }
                else if (settings.UnsafeGuardEnabled)
                {
                    var issues = UnsafeStatements.Find(text);
                    if (issues.Count > 0 && !ConfirmUnsafe(issues))
                    {
                        cancelDefault = true;
                        UnsafeCancelled++;
                        Log.Info("Execução cancelada pelo usuário: " + string.Join("; ", issues));
                        return;
                    }
                }
                if (!settings.CaptureEnabled) return;

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
                    _pending.Add(results, new Pending { Id = entry.Id, Clock = Stopwatch.StartNew(), Editor = editor, Database = entry.Database, TestMode = testMode });
                }
                ExecutionRecorded?.Invoke(this, entry);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao registrar execução no histórico.", ex);
                if (testMode && TestModeController.HasPendingRestore)
                {
                    // não executa sem o ROLLBACK garantido
                    cancelDefault = true;
                    TestModeController.Restore();
                    WarnTestMode("Falha ao preparar o modo de teste; a execução foi cancelada. Detalhes no log.");
                }
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
            if (p.TestMode)
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    if (TestModeController.HasPendingRestore) TestModeController.Restore();
                    if (status == ExecutionStatus.Cancelled)
                        WarnTestMode("Execução cancelada no modo de teste: o ROLLBACK pode não ter rodado e a transação pode ter ficado aberta nesta aba." +
                                     Environment.NewLine + Environment.NewLine + "Execute ROLLBACK nesta aba antes de continuar.");
                }).FileAndForget(ProductInfo.Name + "/TestMode");
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

        private void WarnTestMode(string message)
        {
            LastTestModeWarning = message;
            Log.Info("Modo de teste: " + message.Replace(Environment.NewLine, " "));
            try { Warn?.Invoke(message); }
            catch (Exception ex) { Log.Warn("Falha ao avisar do modo de teste.", ex); }
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
