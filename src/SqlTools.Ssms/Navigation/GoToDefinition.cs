using System;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using SqlTools.Completion;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Ssms.Completion;
using SqlTools.Ssms.Integration;
using Task = System.Threading.Tasks.Task;

namespace SqlTools.Ssms.Navigation
{
    /// <summary>
    /// F12 (comando próprio: o SSMS não tem Edit.GoToDefinition) no nome de um objeto, alias ou coluna:
    /// abre o script da definição numa aba conectada ao mesmo servidor/banco.
    /// </summary>
    internal static class GoToDefinition
    {
        public static void Execute(SqlToolsPackage pkg)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = EditorAccess.GetActiveWpfTextView();
            if (view == null || view.TextBuffer.ContentType.TypeName != SqlContentType.Name)
            {
                pkg.Dte.StatusBar.Text = ProductInfo.Name + ": F12 funciona numa janela de query.";
                return;
            }
            GoTo(pkg, view.TextSnapshot.GetText(), view.Caret.Position.BufferPosition.Position);
        }

        /// <summary>Resolve o objeto na posição e abre a definição. false = não há objeto conhecido ali.</summary>
        internal static bool GoTo(SqlToolsPackage pkg, string text, int position)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var conn = SsmsBridge.GetActiveWindowEditorConnection();
                if (conn == null)
                {
                    pkg.Dte.StatusBar.Text = ProductInfo.Name + ": a janela não está conectada.";
                    return false;
                }
                var md = ThreadHelper.JoinableTaskFactory.Run(() => MetadataService.Instance.GetAsync(conn, TimeSpan.FromSeconds(5), CancellationToken.None));
                var sym = SymbolResolver.At(text, position, md);
                if (sym?.Object == null)
                {
                    pkg.Dte.StatusBar.Text = ProductInfo.Name + ": nenhum objeto do banco sob o cursor.";
                    return false;
                }

                var obj = sym.Object;
                pkg.Dte.StatusBar.Text = ProductInfo.Name + ": gerando o script de " + obj.FullName + "...";
                pkg.JoinableTaskFactory.RunAsync(async () =>
                {
                    string script;
                    try
                    {
                        script = await Task.Run(() => DefinitionScripter.Script(conn, obj));
                    }
                    catch (Exception ex)
                    {
                        Log.Warn("Falha ao gerar o script de " + obj.FullName + ".", ex);
                        await pkg.JoinableTaskFactory.SwitchToMainThreadAsync();
                        pkg.Dte.StatusBar.Text = ProductInfo.Name + ": não foi possível gerar o script de " + obj.FullName + " (" + ex.Message + ").";
                        return;
                    }
                    await pkg.JoinableTaskFactory.SwitchToMainThreadAsync();
                    pkg.Opener.Open(new HistoryEntry { Server = conn.Server, Database = conn.Database, Login = conn.Login, QueryText = script });
                    pkg.Dte.StatusBar.Text = ProductInfo.Name + ": definição de " + obj.FullName + ".";
                }).FileAndForget(ProductInfo.Name + "/GoToDefinition");
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Falha no F12.", ex);
                return false;
            }
        }
    }
}
