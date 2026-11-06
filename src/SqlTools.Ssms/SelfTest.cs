using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Ssms.Integration;
using Task = System.Threading.Tasks.Task;

namespace SqlTools.Ssms
{
    /// <summary>
    /// Teste de integração dentro do SSMS real. Só roda se a variável SQLTOOLS_SELFTEST apontar para uma pasta de saída.
    /// Usa (localdb)\MSSQLLocalDB, ou o servidor em SQLTOOLS_SELFTEST_SERVER.
    /// </summary>
    internal static class SelfTest
    {
        public static void RunIfRequested(SqlToolsPackage pkg)
        {
            var outDir = Environment.GetEnvironmentVariable("SQLTOOLS_SELFTEST");
            if (string.IsNullOrEmpty(outDir)) return;
            Directory.CreateDirectory(outDir);
            ThreadHelper.JoinableTaskFactory.RunAsync(() => RunAsync(pkg, outDir)).FileAndForget("SqlTools/SelfTest");
        }

        private static async Task RunAsync(SqlToolsPackage pkg, string outDir)
        {
            var results = new List<string>();
            var ok = true;
            void Check(string name, bool pass, string detail = "")
            {
                ok &= pass;
                var line = (pass ? "PASS " : "FAIL ") + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail);
                results.Add(line);
                Log.Info("[selftest] " + line);
            }

            try
            {
                var server = Environment.GetEnvironmentVariable("SQLTOOLS_SELFTEST_SERVER");
                if (string.IsNullOrEmpty(server)) server = @"(localdb)\MSSQLLocalDB";
                var marker = Guid.NewGuid().ToString("N").Substring(0, 8);

                await Task.Delay(TimeSpan.FromSeconds(10));
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var dte = pkg.Dte;

                // 1) janela de query conectada
                var ui = SsmsBridge.BuildConnection(server, "", "master");
                Check("BuildConnection", ui != null);
                Check("OpenNewQueryWindow", SsmsBridge.OpenNewQueryWindow(ui));
                await Task.Delay(3000);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var editor = SsmsBridge.GetEditorControl(SsmsBridge.GetActiveDocumentFrame());
                Check("EditorControl", editor != null, editor?.GetType().FullName);
                var conn = editor == null ? null : SsmsBridge.GetEditorConnection(editor);
                Check("EditorConnection", conn != null && conn.Server.Equals(server, StringComparison.OrdinalIgnoreCase), conn?.Server + " / " + conn?.Database + " / " + conn?.Login);

                // 2) execução com sucesso
                var okQuery = "SELECT name, database_id FROM sys.databases; -- selftest " + marker;
                SetText(dte, okQuery);
                dte.ExecuteCommand("Query.Execute");
                var e1 = await WaitForAsync(pkg.History, okQuery);
                Check("Execução registrada", e1 != null);
                Check("Execução concluída com sucesso", e1?.Status == ExecutionStatus.Success, e1 == null ? "" : $"status={e1.Status} dur={e1.DurationMs} rows={e1.RowsAffected} db={e1.Database} login={e1.Login}");
                Check("Duração capturada", e1?.DurationMs != null);
                Check("Banco capturado", !string.IsNullOrEmpty(e1?.Database), e1?.Database);

                // 3) seleção parcial
                var selQuery = "SELECT 42 AS resposta -- selftest-sel " + marker;
                SetText(dte, "SELECT 1 AS nao_executar;\r\n" + selQuery);
                var doc = (TextDocument)dte.ActiveDocument.Object("TextDocument");
                doc.Selection.GotoLine(2, false);
                doc.Selection.EndOfLine(true);
                dte.ExecuteCommand("Query.Execute");
                var e2 = await WaitForAsync(pkg.History, selQuery);
                Check("Só o trecho selecionado foi gravado", e2 != null && e2.QueryText == selQuery, e2?.QueryText);

                // 4) execução com erro
                var errQuery = "SELECT * FROM dbo.tabela_que_nao_existe_" + marker;
                SetText(dte, errQuery);
                dte.ExecuteCommand("Query.Execute");
                var e3 = await WaitForAsync(pkg.History, errQuery);
                Check("Erro registrado como Erro", e3?.Status == ExecutionStatus.Error, e3?.Status.ToString());

                // 4b) comando, atalho e menu registrados
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Command histCmd = null;
                foreach (Command c in dte.Commands)
                {
                    if (string.Equals(c.Guid, PackageGuids.CmdSet.ToString("B"), StringComparison.OrdinalIgnoreCase) && c.ID == PackageIds.ShowHistory) { histCmd = c; break; }
                }
                Check("Comando do histórico registrado", histCmd != null, histCmd?.Name);
                var bindings = histCmd?.Bindings as object[];
                var bindText = bindings == null ? "" : string.Join(" | ", bindings.Select(b => b.ToString()));
                Check("Atalho Ctrl+Q, Ctrl+E", bindText.IndexOf("Ctrl+Q, Ctrl+E", StringComparison.OrdinalIgnoreCase) >= 0, bindText);
                Check("Comando do histórico disponível", histCmd != null && histCmd.IsAvailable);

                // 5) cor da aba
                pkg.TabColorizer.Refresh();
                Check("Abas encontradas", pkg.TabColorizer.LastTabsSeen > 0, "vistas=" + pkg.TabColorizer.LastTabsSeen);
                var expectPaint = pkg.Settings.Current.ColorRules.Count > 0;
                if (expectPaint) Check("Aba colorida", pkg.TabColorizer.LastTabsPainted > 0, "pintadas=" + pkg.TabColorizer.LastTabsPainted);
                await Task.Delay(800);
                Render(System.Windows.Application.Current.MainWindow, Path.Combine(outDir, "1-editor.png"));

                // 6) janela de histórico
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var win = pkg.CreateHistoryWindow(marker);
                win.Show();
                await Task.Delay(2500);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var rows = (win.FindName("Grid") as System.Windows.Controls.DataGrid)?.Items.Count ?? -1;
                Check("Histórico lista as 3 execuções do teste", rows == 3, "linhas=" + rows);
                Render(win, Path.Combine(outDir, "2-historico.png"));
                win.Close();

                var opt = new UI.OptionsWindow(pkg.Settings, () => server);
                opt.Show();
                await Task.Delay(1500);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Render(opt, Path.Combine(outDir, "4-opcoes.png"));
                opt.Close();

                // 7) reabrir do histórico em nova aba conectada
                if (e1 != null)
                {
                    pkg.Opener.Open(e1);
                    await Task.Delay(3000);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var newDoc = dte.ActiveDocument?.Object("TextDocument") as TextDocument;
                    var text = newDoc?.StartPoint.CreateEditPoint().GetText(newDoc.EndPoint);
                    Check("Reabrir: texto", text == okQuery);
                    var ed2 = SsmsBridge.GetEditorControl(SsmsBridge.GetActiveDocumentFrame());
                    var c2 = ed2 == null ? null : SsmsBridge.GetEditorConnection(ed2);
                    Check("Reabrir: conectado no mesmo servidor/banco", c2 != null && c2.Server.Equals(server, StringComparison.OrdinalIgnoreCase) && c2.Database == e1.Database,
                        c2?.Server + " / " + c2?.Database);
                    pkg.TabColorizer.Refresh();
                    await Task.Delay(800);
                    Render(System.Windows.Application.Current.MainWindow, Path.Combine(outDir, "3-reaberta.png"));
                }
            }
            catch (Exception ex)
            {
                Check("Sem exceções", false, ex.ToString());
            }
            finally
            {
                results.Add(ok ? "RESULT: PASS" : "RESULT: FAIL");
                File.WriteAllText(Path.Combine(outDir, "result.txt"), string.Join(Environment.NewLine, results), Encoding.UTF8);
                if (Environment.GetEnvironmentVariable("SQLTOOLS_SELFTEST_EXIT") == "1")
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    try
                    {
                        foreach (Document d in pkg.Dte.Documents.Cast<Document>().ToList()) d.Close(vsSaveChanges.vsSaveChangesNo);
                    }
                    catch { }
                    pkg.Dte.Quit();
                }
            }
        }

        private static void SetText(DTE dte, string text)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var doc = (TextDocument)dte.ActiveDocument.Object("TextDocument");
            var p = doc.StartPoint.CreateEditPoint();
            p.Delete(doc.EndPoint);
            p.Insert(text);
            doc.Selection.StartOfDocument();
        }

        private static async System.Threading.Tasks.Task<HistoryEntry> WaitForAsync(HistoryStore store, string exactText)
        {
            for (int i = 0; i < 60; i++)
            {
                var e = store.Search(new HistoryQuery { Text = exactText, Limit = 5 }).FirstOrDefault(x => x.QueryText == exactText);
                if (e != null && e.Status != ExecutionStatus.Running) return e;
                await Task.Delay(500);
            }
            return store.Search(new HistoryQuery { Text = exactText, Limit = 5 }).FirstOrDefault(x => x.QueryText == exactText);
        }

        /// <summary>Renderiza a janela WPF em PNG (funciona com a tela bloqueada, ao contrário de screenshot).</summary>
        private static void Render(System.Windows.Window w, string path)
        {
            try
            {
                if (w == null) return;
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
                var width = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX);
                var height = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
                if (width <= 0 || height <= 0) return;
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(w);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using (var f = File.Create(path)) enc.Save(f);
            }
            catch (Exception ex)
            {
                Log.Warn("Render falhou: " + path, ex);
                Screenshot(path);
            }
        }

        private static void Screenshot(string path)
        {
            try
            {
                var b = System.Windows.Forms.SystemInformation.VirtualScreen;
                using (var bmp = new Bitmap(b.Width, b.Height))
                {
                    using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(b.Left, b.Top, 0, 0, bmp.Size);
                    bmp.Save(path, ImageFormat.Png);
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Screenshot falhou.", ex);
            }
        }
    }
}
