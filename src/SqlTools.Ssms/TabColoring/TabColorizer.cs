using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using SqlTools.Settings;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.TabColoring
{
    /// <summary>
    /// Pinta a aba de cada janela de query conforme o servidor conectado (regras em Opções).
    /// Desenha por cima da aba com um Adorner: não depende do template do shell, que muda por estado (ativa/inativa).
    /// </summary>
    internal sealed class TabColorizer
    {
        private const string TabTypeName = "DocumentTabItem";

        private readonly SettingsStore _settings;
        private readonly DispatcherTimer _timer;
        private readonly ConditionalWeakTable<FrameworkElement, TabTint> _tints = new ConditionalWeakTable<FrameworkElement, TabTint>();
        private readonly List<WeakReference<TabTint>> _all = new List<WeakReference<TabTint>>();

        /// <summary>Último resultado (para diagnóstico/self-test).</summary>
        public int LastTabsSeen { get; private set; }
        public int LastTabsPainted { get; private set; }

        public TabColorizer(SettingsStore settings)
        {
            _settings = settings;
            _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1.5) };
            _timer.Tick += (s, e) => { ThreadHelper.ThrowIfNotOnUIThread(); Refresh(); };
            _settings.Changed += (s, e) => RefreshSoon();
        }

        public void Start()
        {
            _timer.Start();
            RefreshSoon();
        }

        public void RefreshSoon()
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Refresh();
            });
        }

        public void Refresh()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var s = _settings.Current;
                var active = s.TabColoringEnabled && s.ColorRules.Count > 0;
                int seen = 0, painted = 0;

                if (active && Application.Current != null)
                {
                    foreach (Window w in Application.Current.Windows)
                    {
                        foreach (var tab in FindTabs(w))
                        {
                            seen++;
                            var server = GetServer(tab);
                            var rule = server == null ? null : ColorRule.FindMatch(s.ColorRules, server);
                            if (rule != null && ColorRule.TryParseColor(rule.Color, out var r, out var g, out var b))
                            {
                                Paint(tab, Color.FromRgb(r, g, b));
                                painted++;
                            }
                            else
                            {
                                Clear(tab);
                            }
                        }
                    }
                }
                else
                {
                    ClearAll();
                }

                LastTabsSeen = seen;
                LastTabsPainted = painted;
            }
            catch (Exception ex)
            {
                SsmsBridge.WarnOnce("tabcolor", "Falha ao colorir abas.", ex);
            }
        }

        private static string GetServer(FrameworkElement tab)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = Reflect.Get(tab, "View");
            var frame = Reflect.Get(view, "WindowFrame") as IVsWindowFrame;
            var editor = SsmsBridge.GetEditorControl(frame);
            if (editor == null) return null;
            return SsmsBridge.GetEditorConnection(editor)?.Server;
        }

        private static IEnumerable<FrameworkElement> FindTabs(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node is FrameworkElement fe && fe.GetType().Name == TabTypeName)
                {
                    yield return fe;
                    continue; // não há abas dentro de abas
                }
                int n;
                try { n = VisualTreeHelper.GetChildrenCount(node); }
                catch { continue; }
                for (int i = 0; i < n; i++) stack.Push(VisualTreeHelper.GetChild(node, i));
            }
        }

        private void Paint(FrameworkElement tab, Color color)
        {
            if (_tints.TryGetValue(tab, out var tint))
            {
                // a camada de adorners pode ter sido recriada (aba movida/janela flutuante)
                if (tint.IsAttached)
                {
                    tint.SetColor(color);
                    return;
                }
                _tints.Remove(tab);
            }

            var layer = AdornerLayer.GetAdornerLayer(tab);
            if (layer == null) return;
            tint = new TabTint(tab, color);
            layer.Add(tint);
            _tints.Add(tab, tint);
            _all.Add(new WeakReference<TabTint>(tint));
        }

        private void Clear(FrameworkElement tab)
        {
            if (!_tints.TryGetValue(tab, out var tint)) return;
            tint.Detach();
            _tints.Remove(tab);
        }

        private void ClearAll()
        {
            foreach (var w in _all)
                if (w.TryGetTarget(out var t)) t.Detach();
            _all.Clear();
        }

        /// <summary>Tom suave sobre a aba + faixa sólida embaixo. Não recebe cliques.</summary>
        private sealed class TabTint : Adorner
        {
            private const byte FillAlpha = 0x48;
            private const double StripeHeight = 3;
            private Color _color;

            public TabTint(UIElement tab, Color color) : base(tab)
            {
                _color = color;
                IsHitTestVisible = false;
            }

            public bool IsAttached => AdornerLayer.GetAdornerLayer(AdornedElement) is AdornerLayer layer && VisualTreeHelper.GetParent(this) == layer;

            public void SetColor(Color c)
            {
                if (c == _color) return;
                _color = c;
                InvalidateVisual();
            }

            public void Detach()
            {
                (VisualTreeHelper.GetParent(this) as AdornerLayer)?.Remove(this);
            }

            protected override void OnRender(DrawingContext dc)
            {
                var size = AdornedElement.RenderSize;
                if (size.Width <= 0 || size.Height <= 0) return;
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(FillAlpha, _color.R, _color.G, _color.B)), null, new Rect(size));
                dc.DrawRectangle(new SolidColorBrush(_color), null, new Rect(0, Math.Max(0, size.Height - StripeHeight), size.Width, Math.Min(StripeHeight, size.Height)));
            }
        }
    }
}
