using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms
{
    /// <summary>Abre uma query do histórico numa aba nova, conectada ao mesmo servidor/banco quando possível.</summary>
    internal sealed class QueryOpener
    {
        private readonly DTE _dte;

        public QueryOpener(DTE dte) { _dte = dte; }

        public void Open(HistoryEntry entry)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var ui = SsmsBridge.BuildConnection(entry.Server, entry.Login, entry.Database);
                bool opened;
                try
                {
                    opened = SsmsBridge.OpenNewQueryWindow(ui);
                }
                catch (Exception ex)
                {
                    Log.Warn("Falha ao abrir janela conectada em " + entry.Server + "; abrindo sem conexão.", ex);
                    opened = SsmsBridge.OpenNewQueryWindow(null);
                }

                if (!opened)
                {
                    _dte.ItemOperations.NewFile("General\\Text File", "Query.sql");
                }
                InsertText(entry.QueryText);
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir query do histórico.", ex);
                System.Windows.MessageBox.Show("Não foi possível abrir a query: " + ex.Message, ProductInfo.Name);
            }
        }

        private void InsertText(string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (!(_dte.ActiveDocument?.Object("TextDocument") is TextDocument doc)) return;
            var start = doc.StartPoint.CreateEditPoint();
            start.Delete(doc.EndPoint);
            start.Insert(text ?? "");
            doc.Selection.StartOfDocument();
        }
    }
}
