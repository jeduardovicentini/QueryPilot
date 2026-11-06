using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SqlTools.History;
using SqlTools.Infra;
using SqlTools.Text;

namespace SqlTools.Ssms.UI
{
    public partial class HistoryWindow
    {
        private const string AllDatabases = "(todos)";
        private readonly HistoryStore _store;
        private readonly Action<HistoryEntry> _open;
        private readonly DispatcherTimer _searchDelay;
        private bool _loadingFilters;
        private int _queryVersion;

        public sealed class ServerItem
        {
            public string Name { get; set; }
            public bool IsChecked { get; set; } = true;
        }

        public sealed class Row
        {
            public HistoryEntry Entry { get; set; }
            public string When { get; set; }
            public string Server { get; set; }
            public string Database { get; set; }
            public string Duration { get; set; }
            public string Rows { get; set; }
            public string StatusText { get; set; }
            public string Summary { get; set; }
            public bool IsError { get; set; }
        }

        internal HistoryWindow(HistoryStore store, Action<HistoryEntry> open, string initialSearch = null, bool closedTabs = false)
        {
            _store = store;
            _open = open;
            InitializeComponent();
            Title = ProductInfo.Name + " — Histórico de queries";
            ViewBox.SelectedIndex = closedTabs ? 1 : 0;

            _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _searchDelay.Tick += (s, e) => { _searchDelay.Stop(); Reload(); };

            Loaded += (s, e) =>
            {
                LoadFilters();
                if (!string.IsNullOrEmpty(initialSearch)) SearchBox.Text = initialSearch;
                Reload();
                SearchBox.Focus();
            };
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
            };
        }

        private void LoadFilters()
        {
            _loadingFilters = true;
            try
            {
                ServerList.ItemsSource = _store.GetServers().Select(s => new ServerItem { Name = s }).ToList();
                var dbs = new List<string> { AllDatabases };
                dbs.AddRange(_store.GetDatabases());
                DatabaseBox.ItemsSource = dbs;
                DatabaseBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao carregar filtros do histórico.", ex);
            }
            finally
            {
                _loadingFilters = false;
            }
        }

        private HistoryQuery BuildQuery()
        {
            var closed = ViewBox.SelectedIndex == 1;
            OnlyErrorsBox.IsEnabled = !closed;
            var q = new HistoryQuery { Text = SearchBox.Text, OnlyErrors = !closed && OnlyErrorsBox.IsChecked == true, ClosedTabs = closed };

            var days = int.Parse((string)((ComboBoxItem)PeriodBox.SelectedItem ?? (ComboBoxItem)PeriodBox.Items[1]).Tag, CultureInfo.InvariantCulture);
            if (days == 0) q.FromUtc = DateTime.Today.ToUniversalTime();
            else if (days > 0) q.FromUtc = DateTime.UtcNow.AddDays(-days);

            if (DatabaseBox.SelectedItem is string db && db != AllDatabases) q.Database = db;

            if (ServerList.ItemsSource is List<ServerItem> servers && servers.Any(s => !s.IsChecked))
            {
                q.Servers = servers.Where(s => s.IsChecked).Select(s => s.Name).ToList();
                if (q.Servers.Count == 0) q.Servers.Add("\u0000nenhum");
            }
            return q;
        }

        private async void Reload()
        {
            if (_loadingFilters || !IsLoaded) return;
            var version = ++_queryVersion;
            var q = BuildQuery();
            try
            {
                var entries = await Task.Run(() => _store.Search(q));
                if (version != _queryVersion) return; // chegou uma busca mais nova

                Grid.ItemsSource = entries.Select(ToRow).ToList();
                CountText.Text = entries.Count >= q.Limit
                    ? $"Mostrando as {entries.Count} mais recentes. Refine a busca para ver outras."
                    : q.ClosedTabs ? $"{entries.Count} aba(s) fechada(s)" : $"{entries.Count} execução(ões)";
                if (entries.Count > 0) Grid.SelectedIndex = 0;
                else { Preview.Text = ""; DetailText.Text = ""; }
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao buscar no histórico.", ex);
                CountText.Text = "Erro ao ler o histórico. Veja o log.";
            }
        }

        private static Row ToRow(HistoryEntry e) => new Row
        {
            Entry = e,
            When = FormatWhen(e.ExecutedAtLocal),
            Server = e.Server,
            Database = e.Database,
            Duration = QueryText.Duration(e.DurationMs),
            Rows = e.RowsAffected?.ToString("N0") ?? "",
            StatusText = StatusText(e.Status),
            Summary = QueryText.Summary(e.QueryText),
            IsError = e.Status == ExecutionStatus.Error,
        };

        internal static string StatusText(ExecutionStatus s)
        {
            switch (s)
            {
                case ExecutionStatus.Success: return "OK";
                case ExecutionStatus.Error: return "Erro";
                case ExecutionStatus.Cancelled: return "Cancelada";
                case ExecutionStatus.ClosedTab: return "Fechada";
                default: return "—";
            }
        }

        private static string FormatWhen(DateTime local)
        {
            if (local.Date == DateTime.Today) return "hoje " + local.ToString("HH:mm:ss");
            if (local.Date == DateTime.Today.AddDays(-1)) return "ontem " + local.ToString("HH:mm:ss");
            return local.ToString("dd/MM/yy HH:mm");
        }

        private Row Selected => Grid.SelectedItem as Row;

        private void OnFilterChanged(object sender, RoutedEventArgs e) => Reload();

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            SearchHint.Visibility = string.IsNullOrEmpty(SearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            _searchDelay.Stop();
            _searchDelay.Start();
        }

        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down && Grid.Items.Count > 0)
            {
                Grid.Focus();
                if (Grid.SelectedIndex < 0) Grid.SelectedIndex = 0;
                (Grid.ItemContainerGenerator.ContainerFromIndex(Grid.SelectedIndex) as DataGridRow)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var r = Selected;
            Preview.Text = r?.Entry.QueryText ?? "";
            DetailText.Text = r == null ? "" :
                $"{r.Entry.ExecutedAtLocal:dd/MM/yyyy HH:mm:ss} · {r.Entry.Server} · {r.Entry.Database} · {r.Entry.Login}";
            OpenButton.IsEnabled = CopyButton.IsEnabled = r != null;
        }

        private void OnGridDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindParent<DataGridRow>(d) != null) OpenSelected();
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { OpenSelected(); e.Handled = true; }
            else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control) { CopySelected(); e.Handled = true; }
        }

        private void OnOpen(object sender, RoutedEventArgs e) => OpenSelected();
        private void OnCopy(object sender, RoutedEventArgs e) => CopySelected();
        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OpenSelected()
        {
            var r = Selected;
            if (r == null) return;
            Close();
            _open?.Invoke(r.Entry);
        }

        private void CopySelected()
        {
            var r = Selected;
            if (r == null) return;
            try { Clipboard.SetText(r.Entry.QueryText ?? ""); }
            catch (Exception ex) { Log.Warn("Falha ao copiar para a área de transferência.", ex); }
        }

        private static T FindParent<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            return d as T;
        }
    }
}
