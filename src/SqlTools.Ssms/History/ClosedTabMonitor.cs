using System;
using System.Collections.Generic;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Settings;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.History
{
    /// <summary>
    /// Guarda o texto e a conexão de cada aba de query fechada (inclusive ao fechar o SSMS), para reabrir depois.
    /// No DocumentClosing a janela já saiu da lista de frames; por isso a conexão de cada janela de query é
    /// lembrada antes (ao trocar de janela e ao executar) e, se a janela que fecha ainda é a ativa, lida na hora.
    /// </summary>
    internal sealed class ClosedTabMonitor : IVsWindowFrameEvents
    {
        private readonly DTE _dte;
        private readonly HistoryStore _store;
        private readonly SettingsStore _settings;
        private readonly Dictionary<string, ConnectionSnapshot> _known = new Dictionary<string, ConnectionSnapshot>(StringComparer.OrdinalIgnoreCase);
        private DocumentEvents _events; // manter referência: senão o COM solta o evento

        public ClosedTabMonitor(DTE dte, HistoryStore store, SettingsStore settings)
        {
            _dte = dte;
            _store = store;
            _settings = settings;
        }

        public void Start()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _events = _dte.Events.get_DocumentEvents(null);
            _events.DocumentClosing += OnClosing;
            if (Package.GetGlobalService(typeof(SVsUIShell)) is IVsUIShell7 shell7) shell7.AdviseWindowFrameEvents(this);
            Remember(SsmsBridge.GetActiveDocumentFrame());
        }

        /// <summary>Atualiza a conexão lembrada da janela ativa (chamado depois de cada execução).</summary>
        public void RememberActive()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Remember(SsmsBridge.GetActiveDocumentFrame());
        }

        private void Remember(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var path = PathOf(frame);
                var editor = SsmsBridge.GetEditorControl(frame);
                if (path == null || editor == null) return;
                _known[path] = SsmsBridge.GetEditorConnection(editor) ?? new ConnectionSnapshot();
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao ler a conexão da janela.", ex);
            }
        }

        private static string PathOf(IVsWindowFrame frame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (frame == null) return null;
            return frame.GetProperty((int)__VSFPROPID.VSFPROPID_pszMkDocument, out var mk) == VSConstants.S_OK ? mk as string : null;
        }

        private void OnClosing(Document doc)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var path = doc?.FullName;
                if (string.IsNullOrEmpty(path)) return;

                ConnectionSnapshot conn = null;
                var active = SsmsBridge.GetActiveDocumentFrame();
                if (string.Equals(PathOf(active), path, StringComparison.OrdinalIgnoreCase))
                {
                    var editor = SsmsBridge.GetEditorControl(active);
                    if (editor != null) conn = SsmsBridge.GetEditorConnection(editor) ?? new ConnectionSnapshot();
                }
                if (conn == null && !_known.TryGetValue(path, out conn)) return; // não é janela de query
                _known.Remove(path);

                if (!_settings.Current.ClosedTabsEnabled) return;
                if (!(doc.Object("TextDocument") is TextDocument td)) return;
                var text = td.StartPoint.CreateEditPoint().GetText(td.EndPoint);
                if (string.IsNullOrWhiteSpace(text)) return;
                _store.AddClosedTab(conn.Server, conn.Database, conn.Login, text, DateTime.UtcNow);
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao guardar a aba fechada.", ex);
            }
        }

        // IVsWindowFrameEvents
        public void OnFrameCreated(IVsWindowFrame frame) { }
        public void OnFrameDestroyed(IVsWindowFrame frame) { }
        public void OnFrameIsVisibleChanged(IVsWindowFrame frame, bool newIsVisible) { }
        public void OnFrameIsOnScreenChanged(IVsWindowFrame frame, bool newIsOnScreen) { }

        public void OnActiveFrameChanged(IVsWindowFrame oldFrame, IVsWindowFrame newFrame)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Remember(oldFrame);
            Remember(newFrame);
        }
    }
}
