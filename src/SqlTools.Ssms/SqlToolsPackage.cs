using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Settings;
using SqlTools.Ssms.History;
using SqlTools.Ssms.Integration;
using SqlTools.Ssms.TabColoring;
using SqlTools.Ssms.UI;
using Task = System.Threading.Tasks.Task;

namespace SqlTools.Ssms
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration(ProductInfo.Name, ProductInfo.Description, ProductInfo.Version)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.ShellInitialized_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuids.PackageString)]
    public sealed class SqlToolsPackage : AsyncPackage
    {
        internal static SqlToolsPackage Instance { get; private set; }

        internal DTE Dte { get; private set; }
        internal HistoryStore History { get; private set; }
        internal SettingsStore Settings { get; private set; }
        internal ExecutionMonitor Monitor { get; private set; }
        internal TabColorizer TabColorizer { get; private set; }
        internal QueryOpener Opener { get; private set; }

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Instance = this;
            Log.Info($"Iniciando {ProductInfo.Name} {ProductInfo.Version} (processo {(Environment.Is64BitProcess ? "x64" : "x86")}).");

            try
            {
                // trabalho pesado fora da thread de UI
                try
                {
                    var migrated = AppPaths.MigrateFrom("SqlTools");
                    if (migrated > 0) Log.Info($"Dados migrados da pasta SqlTools ({migrated} arquivo(s)).");
                }
                catch (Exception ex) { Log.Warn("Falha ao migrar dados da pasta SqlTools.", ex); }

                Settings = new SettingsStore(AppPaths.SettingsFile);
                History = new HistoryStore(AppPaths.HistoryDb);
                PurgeOldHistory();
                Log.CleanupOldFiles();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir histórico/configurações. O " + ProductInfo.Name + " ficará inativo.", ex);
                return;
            }

            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            try
            {
                Dte = (DTE)await GetServiceAsync(typeof(DTE));
                Opener = new QueryOpener(Dte);

                if (await GetServiceAsync(typeof(IMenuCommandService)) is OleMenuCommandService mcs)
                {
                    mcs.AddCommand(new MenuCommand((s, e) => ShowHistory(), new CommandID(PackageGuids.CmdSet, PackageIds.ShowHistory)));
                    mcs.AddCommand(new MenuCommand((s, e) => ShowOptions(), new CommandID(PackageGuids.CmdSet, PackageIds.Options)));
                    mcs.AddCommand(new MenuCommand((s, e) => OpenDataFolder(), new CommandID(PackageGuids.CmdSet, PackageIds.OpenDataFolder)));
                    mcs.AddCommand(new MenuCommand((s, e) => RefreshMetadata(), new CommandID(PackageGuids.CmdSet, PackageIds.RefreshMetadata)));
                }

                EnsureHistoryShortcut();
                EnsureTopMenuAllowed();

                Monitor = new ExecutionMonitor(Dte, History, Settings);
                Monitor.Start();

                TabColorizer = new TabColorizer(Settings);
                Monitor.ExecutionRecorded += (s, e) => TabColorizer.RefreshSoon();
                TabColorizer.Start();

                // autocomplete: depois de DDL, recarrega metadados; lista nativa segue a configuração
                Monitor.ExecutionRecorded += (s, e) =>
                {
                    if (e.Status != ExecutionStatus.Running) Completion.MetadataService.Instance.OnExecuted(e.Server, e.Login, e.Database, e.QueryText);
                };
                Completion.NativeIntelliSense.Apply(Settings);
                Settings.Changed += (s, e) => JoinableTaskFactory.RunAsync(async () =>
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    Completion.NativeIntelliSense.Apply(Settings);
                }).FileAndForget(ProductInfo.Name + "/NativeIntelliSense");

                Log.Info(ProductInfo.Name + " pronto. DTE " + Dte.Version + ", " + History.Count() + " execuções no histórico.");
                SelfTest.RunIfRequested(this);
            }
            catch (Exception ex)
            {
                Log.Error("Falha na inicialização do " + ProductInfo.Name + ".", ex);
            }
        }

        /// <summary>
        /// Garante Ctrl+Q, Ctrl+E no histórico uma única vez. O shell não aplica o atalho do .vsct quando outro
        /// comando (ex.: ApexSQL) já o usa. Se o usuário trocar depois, respeitamos.
        /// </summary>
        private void EnsureHistoryShortcut()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var s = Settings.Current;
            if (s.HistoryShortcutApplied) return;
            try
            {
                foreach (Command c in Dte.Commands)
                {
                    if (!string.Equals(c.Guid, PackageGuids.CmdSet.ToString("B"), StringComparison.OrdinalIgnoreCase) || c.ID != PackageIds.ShowHistory) continue;
                    if (!(c.Bindings is object[] b) || b.Length == 0)
                    {
                        c.Bindings = new object[] { "Global::Ctrl+Q, Ctrl+E" };
                        Log.Info("Atalho Ctrl+Q, Ctrl+E atribuído ao histórico.");
                    }
                    break;
                }
                s.HistoryShortcutApplied = true;
                Settings.Save(s);
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível atribuir o atalho do histórico.", ex);
            }
        }

        /// <summary>
        /// O SSMS tira menus de topo de extensões da barra e não os mostra em Extensions; só exibe os que estão em
        /// Menus\AllowedExtensionMenus (o mesmo que Extensions › Customize Menu grava). Libera o nosso uma única vez;
        /// vale a partir do próximo início. Se o usuário desmarcar depois, respeitamos.
        /// </summary>
        private void EnsureTopMenuAllowed()
        {
            var s = Settings.Current;
            if (s.TopMenuAllowed) return;
            try
            {
                var id = PackageGuids.CmdSet.ToString("B").ToUpperInvariant() + "," + PackageIds.TopMenu;
                using (var key = UserRegistryRoot.CreateSubKey(@"Menus\AllowedExtensionMenus"))
                {
                    var current = key.GetValue("") as string ?? "";
                    if (current.IndexOf(id, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        key.SetValue("", current.Length == 0 ? id : current.TrimEnd(';') + ";" + id);
                        Log.Info("Menu " + ProductInfo.Name + " liberado na barra principal (vale no próximo início).");
                    }
                }
                s.TopMenuAllowed = true;
                Settings.Save(s);
            }
            catch (Exception ex)
            {
                Log.Warn("Não foi possível liberar o menu de topo.", ex);
            }
        }

        private void PurgeOldHistory()
        {
            var days = Settings.Current.RetentionDays;
            if (days <= 0) return;
            var removed = History.Purge(DateTime.UtcNow.AddDays(-days));
            if (removed > 0) Log.Info($"Histórico: {removed} execuções com mais de {days} dias removidas.");
        }

        internal HistoryWindow CreateHistoryWindow(string initialSearch = null) =>
            new HistoryWindow(History, entry => { ThreadHelper.ThrowIfNotOnUIThread(); Opener.Open(entry); }, initialSearch);

        internal void ShowHistory()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                CreateHistoryWindow().ShowModal();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir o histórico.", ex);
            }
        }

        private void ShowOptions()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                new OptionsWindow(Settings, CurrentServer).ShowModal();
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao abrir as opções.", ex);
            }
        }

        private static string CurrentServer()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var editor = SsmsBridge.GetEditorControl(SsmsBridge.GetActiveDocumentFrame());
            return (editor != null ? SsmsBridge.GetEditorConnection(editor) : SsmsBridge.GetActiveWindowConnection())?.Server;
        }

        private static void OpenDataFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + AppPaths.Root + "\""); }
            catch (Exception ex) { Log.Warn("Falha ao abrir a pasta de dados.", ex); }
        }

        /// <summary>Descarta os metadados do banco da janela ativa (ou de todos) e recarrega em segundo plano.</summary>
        private void RefreshMetadata()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var conn = SsmsBridge.GetActiveWindowEditorConnection();
            if (conn == null)
            {
                Completion.MetadataService.Instance.InvalidateAll();
                Dte.StatusBar.Text = ProductInfo.Name + ": metadados descartados; recarregam na próxima lista.";
                return;
            }
            Completion.MetadataService.Instance.Invalidate(conn);
            Dte.StatusBar.Text = ProductInfo.Name + ": recarregando metadados de " + conn.Server + "/" + conn.Database + "...";
            _ = Completion.MetadataService.Instance.GetAsync(conn, TimeSpan.Zero, CancellationToken.None);
        }
    }

    internal static class PackageGuids
    {
        public const string PackageString = "3e8f2a41-7c5d-4b9e-a1f6-2d4c8b7e9a10";
        public const string CmdSetString = "9b2c4d6e-1a3f-4e5b-8c7d-6f0a2b4c8e12";
        public static readonly Guid CmdSet = new Guid(CmdSetString);
    }

    internal static class PackageIds
    {
        public const int ShowHistory = 0x0100;
        public const int Options = 0x0101;
        public const int OpenDataFolder = 0x0102;
        public const int RefreshMetadata = 0x0103;
        public const int TopMenu = 0x1000;
    }
}
