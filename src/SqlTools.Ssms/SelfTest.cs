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
using EditorAccess = SqlTools.Ssms.Integration.EditorAccess;
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

                // 1b) autocomplete
                await CompletionChecksAsync(pkg, dte, Check);
                if (Environment.GetEnvironmentVariable("SQLTOOLS_SELFTEST_SCOPE") == "completion") return;

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
                // o perfil do SSMS é o do usuário: devolve a lista nativa como estava antes do teste
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    var original = pkg.Settings.Current.NativeAutoListOriginal;
                    if (original >= 0) Completion.NativeIntelliSense.SetAutoList(original == 1);
                }
                catch (Exception ex) { Log.Warn("[selftest] não restaurou a lista nativa.", ex); }

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

        private const string CompletionDb = "QueryPilotSelfTest";

        /// <summary>Autocomplete no banco de teste criado pelo Run-SelfTest.ps1 (Customer, Order, OrderItem, sales.Product).</summary>
        private static async Task CompletionChecksAsync(SqlToolsPackage pkg, DTE dte, Action<string, bool, string> check)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var server = Environment.GetEnvironmentVariable("SQLTOOLS_SELFTEST_SERVER");
            if (string.IsNullOrEmpty(server)) server = @"(localdb)\MSSQLLocalDB";
            SsmsBridge.OpenNewQueryWindow(SsmsBridge.BuildConnection(server, "", CompletionDb));
            await Task.Delay(3000);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var conn = SsmsBridge.GetActiveWindowEditorConnection();
            check("Completion: janela no banco de teste", conn != null && conn.Database == CompletionDb, conn?.Database);
            check("Completion: lista nativa automática desligada", Completion.NativeIntelliSense.GetAutoList() == false, "");

            var view = EditorAccess.GetActiveWpfTextView();
            check("Completion: editor SQL", view?.TextBuffer.ContentType.TypeName == Completion.SqlContentType.Name, view?.TextBuffer.ContentType.TypeName);
            if (view == null) return;

            async System.Threading.Tasks.Task<List<string>> ItemsAt(string textWithCaret)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var caret = textWithCaret.IndexOf('|');
                SetText(dte, textWithCaret.Remove(caret, 1));
                view.Caret.MoveTo(new Microsoft.VisualStudio.Text.SnapshotPoint(view.TextSnapshot, caret));
                // a primeira lista pode chegar antes dos metadados; tenta de novo
                List<string> items = null;
                for (int i = 0; i < 4; i++)
                {
                    items = await EditorAccess.CollectCompletionItemsAsync(view);
                    if (items.Count > 0 && items.Any(x => !SqlTools.Sql.SqlKeywords.Suggested.Contains(x))) break;
                    await Task.Delay(1500);
                }
                Log.Info("[selftest] " + textWithCaret.Replace("\n", " ") + " → " + string.Join(" | ", items.Take(12)));
                return items;
            }

            var join = await ItemsAt("SELECT * FROM Customer c JOIN |");
            check("Completion: JOIN pela FK", join.Contains("[Order] o ON o.CustomerID = c.CustomerID"), join.FirstOrDefault());
            var cols = await ItemsAt("SELECT c.| FROM Customer c");
            check("Completion: colunas pelo alias", cols.Count == 3 && new[] { "CustomerID", "Name", "Email" }.All(cols.Contains), string.Join(",", cols));
            var star = await ItemsAt("SELECT *| FROM Customer");
            check("Completion: expandir *", star.Contains("* → 3 colunas"), string.Join(",", star));
            var exec = await ItemsAt("EXEC |");
            check("Completion: procedures", exec.Contains("usp_GetCustomer"), "");
            var sch = await ItemsAt("SELECT * FROM sales.|");
            check("Completion: objetos do schema", sch.Contains("Product"), string.Join(",", sch));
            var pars = await ItemsAt("EXEC usp_GetCustomer |");
            check("Completion: parâmetros da procedure", pars.Contains("@id"), string.Join(",", pars));
            var tmp = await ItemsAt("CREATE TABLE #t (Id int, Nome varchar(10));\nSELECT t.| FROM #t t");
            check("Completion: colunas da tabela temporária", tmp.Count == 2 && tmp.Contains("Nome"), string.Join(",", tmp));
            var sysv = await ItemsAt("SELECT * FROM sys.|");
            check("Completion: views de sistema (sys.)", sysv.Contains("objects") && sysv.Contains("tables"), sysv.Count + " itens");

            // digitação real: a lista abre sozinha depois de "FROM " e o Tab insere a tabela com alias
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            SetText(dte, "");
            var typed = await EditorAccess.TypeAsync(view, "SELECT * FROM Cust");
            var broker = EditorAccess.ComponentModel.GetService<Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.IAsyncCompletionBroker>();
            check("Completion: lista abriu ao digitar", broker.IsCompletionActive(view), typed);
#pragma warning disable CS0618 // broker legado: é por ele que a lista nativa aparece
            var legacy = EditorAccess.ComponentModel.GetService<Microsoft.VisualStudio.Language.Intellisense.ICompletionBroker>();
            foreach (var ls in legacy?.GetSessions(view) ?? (IEnumerable<Microsoft.VisualStudio.Language.Intellisense.ICompletionSession>)new Microsoft.VisualStudio.Language.Intellisense.ICompletionSession[0])
                Log.Info("[selftest] sessão legada " + ls.GetType().FullName + ": " + string.Join(" | ", ls.CompletionSets.Select(cs => cs.GetType().Name + "[" + string.Join(",", cs.Completions.Take(8).Select(c => c.DisplayText)) + "]")));
            var asyncSession = broker.GetSession(view);
            if (asyncSession != null)
                Log.Info("[selftest] sessão async: " + string.Join(" | ", asyncSession.GetComputedItems(System.Threading.CancellationToken.None).Items.Take(8).Select(i => i.DisplayText)));
            // o broker legado também enxerga a nossa sessão (ShimCompletionSession); nativa seria outra
            var native = ((IEnumerable<Microsoft.VisualStudio.Language.Intellisense.ICompletionSession>)legacy?.GetSessions(view) ?? new Microsoft.VisualStudio.Language.Intellisense.ICompletionSession[0])
                .Where(x => !x.IsDismissed && x.GetType().Name != "ShimCompletionSession").ToList();
            check("Completion: lista nativa não abriu junto", native.Count == 0, string.Join(",", native.Select(x => x.GetType().Name)));
#pragma warning restore CS0618
            await EditorAccess.SendTabAsync(view);
            await Task.Delay(500);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var final = view.TextSnapshot.GetText();
            check("Completion: Tab insere tabela com alias", final == "SELECT * FROM Customer c", final);

            // snippet: ssf + Tab
            SetText(dte, "");
            await EditorAccess.TypeAsync(view, "ssf");
            await EditorAccess.SendTabAsync(view);
            await Task.Delay(500);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var snip = view.TextSnapshot.GetText();
            check("Completion: snippet ssf + Tab", snip == "SELECT * FROM", snip);

            // Tab com o cursor no *: expande as colunas e cria o alias
            SetText(dte, "SELECT * FROM Customer");
            view.Caret.MoveTo(new Microsoft.VisualStudio.Text.SnapshotPoint(view.TextSnapshot, 7));
            await EditorAccess.SendTabAsync(view);
            await Task.Delay(500);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var expanded = view.TextSnapshot.GetText();
            check("Completion: Tab no * expande com alias", expanded == "SELECT c.CustomerID, c.Name, c.Email FROM Customer c", expanded);

            // INSERT INTO tabela + Tab (digitado: a lista está aberta com o nome inteiro)
            SetText(dte, "");
            await EditorAccess.TypeAsync(view, "INSERT INTO Customer");
            await EditorAccess.SendTabAsync(view);
            await Task.Delay(500);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var ins = view.TextSnapshot.GetText().Replace("\r\n", "\n");
            var insExpected = "INSERT INTO [dbo].[Customer]\n           ([Name]\n           ,[Email])\n     VALUES\n           (<Name>\n           ,<Email>)";
            check("Completion: INSERT INTO tabela + Tab gera o INSERT", ins == insExpected, ins.Replace("\n", "⏎"));
            check("Completion: primeiro valor selecionado", view.Selection.SelectedSpans.FirstOrDefault().GetText() == "<Name>", view.Selection.SelectedSpans.FirstOrDefault().GetText());

            // recarga a quente (Atualizar metadados)
            var c2 = SsmsBridge.GetActiveWindowEditorConnection();
            Completion.MetadataService.Instance.Invalidate(c2);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var md = await Completion.MetadataService.Instance.GetAsync(c2, TimeSpan.FromSeconds(60), System.Threading.CancellationToken.None);
            check("Completion: recarga de metadados", md != null && md.ForeignKeys.Count == 3, sw.ElapsedMilliseconds + " ms");

            await FormatChecksAsync(dte, view, check);
            await SafetyChecksAsync(pkg, dte, server, check);
            SetText(dte, "");
        }

        /// <summary>Alerta de DELETE sem WHERE (cancela e confirma, conferindo no banco) e reabrir aba fechada.</summary>
        private static async Task SafetyChecksAsync(SqlToolsPackage pkg, DTE dte, string server, Action<string, bool, string> check)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            long Count(string sql)
            {
                using (var c = SsmsBridge.CreateDbConnection(SsmsBridge.BuildConnection(server, "", CompletionDb), CompletionDb))
                {
                    c.Open();
                    using (var cmd = c.CreateCommand())
                    {
                        cmd.CommandText = sql;
                        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
                    }
                }
            }
            Count("IF OBJECT_ID('dbo.GuardProbe') IS NOT NULL DROP TABLE dbo.GuardProbe; CREATE TABLE dbo.GuardProbe (i int); INSERT dbo.GuardProbe VALUES (1), (2); SELECT 0");

            var original = pkg.Monitor.ConfirmUnsafe;
            string asked = null;
            try
            {
                var before = pkg.Monitor.UnsafeCancelled;
                pkg.Monitor.ConfirmUnsafe = issues => { asked = string.Join("; ", issues); return false; };
                SetText(dte, "SELECT 1;\r\nDELETE FROM dbo.GuardProbe");
                dte.ExecuteCommand("Query.Execute");
                await Task.Delay(2500);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                check("Alerta: perguntou antes do DELETE sem WHERE", asked == "DELETE em dbo.GuardProbe (linha 2)", asked);
                check("Alerta: 'Não' cancela a execução", pkg.Monitor.UnsafeCancelled == before + 1 && Count("SELECT COUNT(*) FROM dbo.GuardProbe") == 2, "cancelamentos=" + (pkg.Monitor.UnsafeCancelled - before));

                pkg.Monitor.ConfirmUnsafe = issues => true;
                var sure = "DELETE FROM dbo.GuardProbe -- confirmado";
                SetText(dte, sure);
                dte.ExecuteCommand("Query.Execute");
                var done = await WaitForAsync(pkg.History, sure);
                check("Alerta: 'Sim' executa", done?.Status == ExecutionStatus.Success && Count("SELECT COUNT(*) FROM dbo.GuardProbe") == 0, done?.Status.ToString());
            }
            finally
            {
                pkg.Monitor.ConfirmUnsafe = original;
            }

            // aba fechada: guarda texto + conexão e reabre conectada
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var tabText = "SELECT 'aba fechada " + Guid.NewGuid().ToString("N").Substring(0, 8) + "' AS teste";
            SetText(dte, tabText);
            dte.ActiveDocument.Close(vsSaveChanges.vsSaveChangesNo);
            await Task.Delay(800);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var saved = pkg.History.Search(new HistoryQuery { ClosedTabs = true, Text = tabText }).FirstOrDefault();
            check("Abas: texto e banco guardados ao fechar", saved != null && saved.QueryText == tabText && saved.Database == CompletionDb, saved?.Server + "/" + saved?.Database);

            object cin = null, cout = null;
            dte.Commands.Raise(PackageGuids.CmdSet.ToString("B"), PackageIds.ReopenClosedTab, ref cin, ref cout);
            await Task.Delay(3000);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var doc = (TextDocument)dte.ActiveDocument.Object("TextDocument");
            var reopenedText = doc.StartPoint.CreateEditPoint().GetText(doc.EndPoint);
            var rc = SsmsBridge.GetActiveWindowEditorConnection();
            check("Abas: Ctrl+Q, Ctrl+T reabre no mesmo banco", reopenedText == tabText && rc?.Database == CompletionDb, rc?.Database + " | " + reopenedText);
            Count("DROP TABLE dbo.GuardProbe; SELECT 0");
        }

        /// <summary>Formatador pelo comando real: documento inteiro, Ctrl+Z, só a seleção e atalho.</summary>
        private static async Task FormatChecksAsync(DTE dte, Microsoft.VisualStudio.Text.Editor.IWpfTextView view, Action<string, bool, string> check)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            async System.Threading.Tasks.Task<string> RunFormat()
            {
                object cin = null, cout = null;
                dte.Commands.Raise(PackageGuids.CmdSet.ToString("B"), PackageIds.FormatSql, ref cin, ref cout);
                await Task.Delay(300);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                return view.TextSnapshot.GetText().Replace("\r\n", "\n");
            }

            var original = "select c.Name, o.OrderID from Customer c join [Order] o on o.CustomerID = c.CustomerID where c.CustomerID = 1";
            SetText(dte, original);
            var doc = await RunFormat();
            var expected = "SELECT\n    c.Name,\n    o.OrderID\nFROM Customer c\nJOIN [Order] o\n    ON o.CustomerID = c.CustomerID\nWHERE c.CustomerID = 1";
            check("Formatar: documento inteiro", doc == expected, doc.Replace("\n", "⏎"));

            dte.ExecuteCommand("Edit.Undo");
            await Task.Delay(200);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            check("Formatar: um Ctrl+Z desfaz tudo", view.TextSnapshot.GetText() == original, view.TextSnapshot.GetText());

            SetText(dte, "select 1\r\n  select a, b from t\r\nselect 2");
            view.Selection.Select(view.TextSnapshot.GetLineFromLineNumber(1).Extent, false);
            var part = await RunFormat();
            check("Formatar: só a seleção (mantém o recuo)", part == "select 1\n  SELECT\n      a,\n      b\n  FROM t\nselect 2", part.Replace("\n", "⏎"));

            SetText(dte, "select 'sem fim from t");
            var bad = await RunFormat();
            check("Formatar: SQL incompleto fica intacto", bad == "select 'sem fim from t", bad);

            Command fmtCmd = null;
            foreach (Command c in dte.Commands)
                if (string.Equals(c.Guid, PackageGuids.CmdSet.ToString("B"), StringComparison.OrdinalIgnoreCase) && c.ID == PackageIds.FormatSql) { fmtCmd = c; break; }
            var b = fmtCmd?.Bindings as object[];
            var bt = b == null ? "" : string.Join(" | ", b.Select(x => x.ToString()));
            check("Formatar: atalho Ctrl+K, Ctrl+Y", bt.IndexOf("Ctrl+K, Ctrl+Y", StringComparison.OrdinalIgnoreCase) >= 0, fmtCmd?.Name + " " + bt);
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
