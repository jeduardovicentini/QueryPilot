using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using SqlTools.Infra;
using SqlTools.Settings;

namespace SqlTools.Ssms.UI
{
    public partial class OptionsWindow
    {
        private readonly SettingsStore _store;
        private readonly Func<string> _currentServer;
        private readonly ObservableCollection<ColorRule> _rules;

        public IReadOnlyList<KeyValuePair<string, string>> Presets => ColorRule.Presets;

        internal OptionsWindow(SettingsStore store, Func<string> currentServer)
        {
            _store = store;
            _currentServer = currentServer;
            InitializeComponent();
            DataContext = this;
            Title = ProductInfo.Name + " — Opções";

            var s = store.Current;
            CompletionBox.Content = "Usar o autocomplete do " + ProductInfo.Name + " (desliga só a lista automática do IntelliSense nativo)";
            CompletionBox.IsChecked = s.CompletionEnabled;
            AutoAliasBox.IsChecked = s.CompletionAutoAlias;
            CaptureBox.IsChecked = s.CaptureEnabled;
            RetentionBox.Text = s.RetentionDays.ToString(CultureInfo.InvariantCulture);
            TabColorBox.IsChecked = s.TabColoringEnabled;
            _rules = new ObservableCollection<ColorRule>(s.ColorRules);
            RulesGrid.ItemsSource = _rules;
            DataPathText.Text = "Dados: " + AppPaths.Root;
        }

        private void OnAddRule(object sender, RoutedEventArgs e) => AddRule(new ColorRule { ServerPattern = "", Color = "#D32F2F" });

        private void OnAddCurrent(object sender, RoutedEventArgs e)
        {
            var server = _currentServer?.Invoke();
            if (string.IsNullOrEmpty(server))
            {
                MessageBox.Show(this, "Nenhuma janela de query conectada está ativa.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            AddRule(new ColorRule { ServerPattern = server, Color = "#D32F2F" });
        }

        private void AddRule(ColorRule r)
        {
            _rules.Add(r);
            RulesGrid.SelectedItem = r;
            RulesGrid.ScrollIntoView(r);
        }

        private void OnRemoveRule(object sender, RoutedEventArgs e)
        {
            if (RulesGrid.SelectedItem is ColorRule r) _rules.Remove(r);
        }

        private void OnMoveUp(object sender, RoutedEventArgs e) => Move(-1);
        private void OnMoveDown(object sender, RoutedEventArgs e) => Move(+1);

        private void Move(int delta)
        {
            if (!(RulesGrid.SelectedItem is ColorRule r)) return;
            var i = _rules.IndexOf(r);
            var j = i + delta;
            if (j < 0 || j >= _rules.Count) return;
            _rules.Move(i, j);
            RulesGrid.SelectedItem = r;
        }

        private void OnSave(object sender, RoutedEventArgs e)
        {
            RulesGrid.CommitEdit();
            RulesGrid.CommitEdit();

            if (!int.TryParse(RetentionBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) || days < 0)
            {
                MessageBox.Show(this, "Informe um número de dias válido (0 ou mais).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                RetentionBox.Focus();
                return;
            }

            var s = _store.Current;
            s.CompletionEnabled = CompletionBox.IsChecked == true;
            s.CompletionAutoAlias = AutoAliasBox.IsChecked == true;
            s.CaptureEnabled = CaptureBox.IsChecked == true;
            s.RetentionDays = days;
            s.TabColoringEnabled = TabColorBox.IsChecked == true;
            s.ColorRules = _rules.Where(r => !string.IsNullOrWhiteSpace(r.ServerPattern)).Select(r => r.Clone()).ToList();
            try
            {
                _store.Save(s);
                DialogResult = true;
            }
            catch (Exception ex)
            {
                Log.Error("Falha ao salvar configurações.", ex);
                MessageBox.Show(this, "Não foi possível salvar: " + ex.Message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
