using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Utilities;
using SqlTools.Infra;
using SqlTools.Safety;
using SqlTools.Ssms.Completion;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Safety
{
    /// <summary>
    /// Modo de teste por aba: antes de executar, envolve o texto em BEGIN TRAN ... ROLLBACK, seleciona o
    /// texto envolvido (o SSMS executa a seleção) e, assim que o SSMS lê o texto, desfaz a alteração.
    /// </summary>
    internal static class TestModeController
    {
        private static readonly object Key = typeof(TestModeController);

        public static event EventHandler<ITextBuffer> Changed;

        public static bool IsOn(ITextBuffer buffer) =>
            buffer != null && buffer.Properties.TryGetProperty(Key, out bool on) && on;

        public static void Set(ITextBuffer buffer, bool on)
        {
            if (buffer == null) return;
            buffer.Properties[Key] = on;
            Changed?.Invoke(null, buffer);
        }

        /// <summary>Comando do menu (Ctrl+Q, Ctrl+R): liga/desliga na aba ativa.</summary>
        public static void Toggle(SqlToolsPackage pkg)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var view = EditorAccess.GetActiveWpfTextView();
            if (view == null || !view.TextBuffer.ContentType.IsOfType(SqlContentType.Name))
            {
                pkg.Dte.StatusBar.Text = ProductInfo.Name + ": abra uma aba de query para usar o modo de teste.";
                return;
            }
            var on = !IsOn(view.TextBuffer);
            Set(view.TextBuffer, on);
            pkg.Dte.StatusBar.Text = ProductInfo.Name + (on
                ? ": modo de teste LIGADO nesta aba. Tudo o que executar será desfeito (ROLLBACK)."
                : ": modo de teste desligado nesta aba.");
            Log.Info("Modo de teste " + (on ? "ligado." : "desligado."));
        }

        private static Pending _pending;

        private sealed class Pending
        {
            public IWpfTextView View;
            public ITextUndoHistory History;
            public ITextVersion VersionAfterEdit;
            public ITrackingSpan PrefixSpan, SuffixSpan;
            public ITrackingPoint Anchor, Active;
            public bool WasEmpty;
        }

        public static bool HasPendingRestore => _pending != null;

        /// <summary>
        /// Envolve o texto que será executado. Devolve a mensagem de erro (e não altera nada) quando não dá
        /// para garantir o ROLLBACK; null quando preparou.
        /// </summary>
        public static string Prepare(IWpfTextView view)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Restore(); // sobra de uma execução anterior
            var snapshot = view.TextBuffer.CurrentSnapshot;
            var sel = view.Selection;
            if (!sel.IsEmpty && sel.Mode == TextSelectionMode.Box)
                return "O modo de teste não funciona com seleção em bloco (Alt+arrastar). Selecione as linhas normalmente.";

            var span = sel.IsEmpty || sel.StreamSelectionSpan.SnapshotSpan.IsEmpty
                ? new SnapshotSpan(snapshot, 0, snapshot.Length)
                : sel.StreamSelectionSpan.SnapshotSpan.TranslateTo(snapshot, SpanTrackingMode.EdgeInclusive);
            var text = span.GetText();
            if (TestMode.HasTransactionControl(text))
                return "O texto tem COMMIT ou ROLLBACK; o modo de teste não consegue garantir que tudo seja desfeito." +
                       Environment.NewLine + Environment.NewLine + "Execução cancelada. Desligue o modo de teste (Ctrl+Q, Ctrl+R) para executar assim.";
            var newLine = snapshot.GetText().Contains("\r\n") || !snapshot.GetText().Contains("\n") ? "\r\n" : "\n";
            var wrap = TestMode.Wrap(text, newLine);
            if (wrap == null)
                return "O texto termina dentro de um comentário ou de uma string; o modo de teste não consegue acrescentar o ROLLBACK.";

            var p = new Pending
            {
                View = view,
                WasEmpty = sel.IsEmpty,
                Anchor = snapshot.CreateTrackingPoint(sel.AnchorPoint.Position.TranslateTo(snapshot, PointTrackingMode.Negative), PointTrackingMode.Negative),
                Active = snapshot.CreateTrackingPoint(sel.ActivePoint.Position.TranslateTo(snapshot, PointTrackingMode.Negative), PointTrackingMode.Negative),
            };
            var registry = EditorAccess.ComponentModel?.GetService<ITextUndoHistoryRegistry>();
            if (registry != null && registry.TryGetHistory(view.TextBuffer, out var history)) p.History = history;

            int start = span.Start.Position, end = span.End.Position;
            ITextSnapshot after;
            using (var tx = p.History?.CreateTransaction(ProductInfo.Name + " - modo de teste"))
            {
                using (var edit = view.TextBuffer.CreateEdit())
                {
                    edit.Insert(end, wrap.Suffix);
                    edit.Insert(start, wrap.Prefix);
                    after = edit.Apply();
                    if (edit.Canceled || after == snapshot) return "Não foi possível preparar o modo de teste (editor somente leitura?).";
                }
                tx?.Complete();
            }
            p.VersionAfterEdit = after.Version;
            _pending = p; // daqui em diante, Restore() desfaz
            p.PrefixSpan = after.CreateTrackingSpan(start, wrap.Prefix.Length, SpanTrackingMode.EdgeExclusive);
            var suffixStart = end + wrap.Prefix.Length;
            p.SuffixSpan = after.CreateTrackingSpan(suffixStart, wrap.Suffix.Length, SpanTrackingMode.EdgeExclusive);
            view.Selection.Select(new SnapshotSpan(after, start, suffixStart + wrap.Suffix.Length - start), false);
            view.Caret.MoveTo(new SnapshotPoint(after, suffixStart + wrap.Suffix.Length));
            return null;
        }

        /// <summary>Desfaz o BEGIN TRAN/ROLLBACK inserido e volta a seleção original. Devolve false se não conseguiu.</summary>
        public static bool Restore()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var p = _pending;
            if (p == null) return true;
            try
            {
                var buffer = p.View.TextBuffer;
                var current = buffer.CurrentSnapshot;
                if (p.History != null && current.Version == p.VersionAfterEdit && p.History.CanUndo)
                {
                    p.History.Undo(1);
                }
                else
                {
                    using (var edit = buffer.CreateEdit())
                    {
                        edit.Delete(p.SuffixSpan.GetSpan(current));
                        edit.Delete(p.PrefixSpan.GetSpan(current));
                        edit.Apply();
                        if (edit.Canceled) return false;
                    }
                }
                var snap = buffer.CurrentSnapshot;
                if (snap.GetText().Contains(TestMode.Message)) return false;
                var anchor = p.Anchor.GetPoint(snap);
                var active = p.Active.GetPoint(snap);
                if (p.WasEmpty) p.View.Selection.Clear();
                else p.View.Selection.Select(new VirtualSnapshotPoint(anchor), new VirtualSnapshotPoint(active));
                p.View.Caret.MoveTo(active);
                _pending = null;
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn("Falha ao restaurar o texto depois do modo de teste.", ex);
                return false;
            }
        }
    }

    /// <summary>Faixa no canto do editor enquanto o modo de teste estiver ligado.</summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class TestModeBanner : IWpfTextViewCreationListener
    {
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("QueryPilotTestMode")]
        [Order(After = PredefinedAdornmentLayers.Caret)]
        public AdornmentLayerDefinition Layer = null;

        public void TextViewCreated(IWpfTextView view)
        {
            var layer = view.GetAdornmentLayer("QueryPilotTestMode");
            Border banner = null;

            void Place()
            {
                if (banner == null) return;
                banner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(banner, Math.Max(0, view.ViewportWidth - banner.DesiredSize.Width - 24));
                Canvas.SetTop(banner, 4);
            }

            void Update()
            {
                var on = TestModeController.IsOn(view.TextBuffer);
                if (on && banner == null)
                {
                    banner = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),
                        CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(8, 3, 8, 3),
                        Opacity = 0.92,
                        IsHitTestVisible = false,
                        Child = new TextBlock
                        {
                            Text = "MODO DE TESTE — tudo é desfeito (ROLLBACK)",
                            Foreground = Brushes.White,
                            FontWeight = FontWeights.SemiBold,
                            FontSize = 11,
                        },
                    };
                    layer.AddAdornment(AdornmentPositioningBehavior.ViewportRelative, null, Key, banner, null);
                    Place();
                }
                else if (!on && banner != null)
                {
                    layer.RemoveAdornmentsByTag(Key);
                    banner = null;
                }
            }

            EventHandler<ITextBuffer> changed = (s, b) => { if (b == view.TextBuffer) Update(); };
            TestModeController.Changed += changed;
            view.ViewportWidthChanged += (s, e) => Place();
            view.Closed += (s, e) => TestModeController.Changed -= changed;
            Update();
        }

        private static readonly object Key = new object();

        /// <summary>Self-test: a faixa está visível na view?</summary>
        internal static bool IsShown(IWpfTextView view) =>
            view.GetAdornmentLayer("QueryPilotTestMode").Elements.Any();
    }
}
