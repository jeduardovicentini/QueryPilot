using System;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using SqlTools.Completion;
using SqlTools.Infra;
using SqlTools.Metadata;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Completion
{
    /// <summary>
    /// Expansões pelo Tab (fora disso, o Tab segue normal):
    /// - cursor no * → colunas com alias (SELECT * FROM oCarrinho → SELECT oc.Col1, ... FROM oCarrinho oc);
    /// - fim de "INSERT INTO tabela" → INSERT completo com colunas e &lt;valores&gt;.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [ContentType(SqlContentType.Name)]
    [Name("QueryPilotTabExpansion")]
    [Order(Before = PredefinedCompletionNames.CompletionCommandHandler)]
    internal sealed class TabExpansionCommandHandler : ICommandHandler<TabKeyCommandArgs>
    {
        private static readonly TimeSpan MetadataWait = TimeSpan.FromSeconds(3);

        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => ProductInfo.Name + ": expansões no Tab";

        public CommandState GetCommandState(TabKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TabKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = SqlToolsPackage.Instance?.Settings?.Current;
                if (settings == null || !settings.CompletionEnabled) return false;
                var view = args.TextView;
                if (!view.Selection.IsEmpty) return false;

                var buffer = args.SubjectBuffer;
                var point = view.Caret.Position.Point.GetPoint(buffer, PositionAffinity.Predecessor);
                if (point == null) return false;
                var snapshot = buffer.CurrentSnapshot;
                int caret = point.Value.Position;
                bool listOpen = Broker != null && Broker.IsCompletionActive(view);

                bool atStar = !listOpen && ((caret < snapshot.Length && snapshot[caret] == '*') || (caret > 0 && snapshot[caret - 1] == '*'));
                bool afterWord = caret > 0 && (char.IsLetterOrDigit(snapshot[caret - 1]) || snapshot[caret - 1] == ']' || snapshot[caret - 1] == '_' || snapshot[caret - 1] == ' ');
                if (!atStar && !afterWord) return false;

                // antes de consultar metadados: o texto tem cara de INSERT?
                var text = snapshot.GetText();
                if (!atStar && text.LastIndexOf("INSERT", caret - 1, Math.Min(caret, 400), StringComparison.OrdinalIgnoreCase) < 0) return false;

                var md = Metadata();
                if (md == null) return false;

                if (atStar)
                {
                    var edits = CompletionEngine.ExpandStar(text, caret, md);
                    if (edits == null) return false;
                    var star = edits.First(e => e.IsColumns);
                    int shift = edits.Where(e => e.Start < star.Start).Sum(e => e.Text.Length - e.Length);
                    Apply(buffer, edits);
                    view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, Math.Min(star.Start + shift + star.Text.Length, buffer.CurrentSnapshot.Length)));
                    return true;
                }

                // com a lista aberta, só expande se o nome digitado já é a tabela inteira; senão o Tab escolhe o item
                var ins = CompletionEngine.ExpandInsert(text, caret, md);
                if (ins == null) return false;
                if (listOpen) Broker.GetSession(view)?.Dismiss();
                Apply(buffer, new[] { ins });
                var now = buffer.CurrentSnapshot;
                if (ins.SelectOffset >= 0)
                {
                    var sel = new SnapshotSpan(now, ins.Start + ins.SelectOffset, ins.SelectLength);
                    view.Selection.Select(sel, false);
                    view.Caret.MoveTo(sel.End);
                }
                view.ViewScroller.EnsureSpanVisible(new SnapshotSpan(view.Caret.Position.BufferPosition, 0));
                return true;
            }
            catch (Exception ex)
            {
                SsmsBridge.WarnOnce("tab-expansion", "Falha na expansão do Tab.", ex);
                return false;
            }
        }

        private static DatabaseMetadata Metadata()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var conn = SsmsBridge.GetActiveWindowEditorConnection();
            if (conn == null) return null;
            return ThreadHelper.JoinableTaskFactory.Run(() => MetadataService.Instance.GetAsync(conn, MetadataWait, CancellationToken.None));
        }

        private static void Apply(ITextBuffer buffer, System.Collections.Generic.IEnumerable<TextEdit> edits)
        {
            using (var edit = buffer.CreateEdit())
            {
                foreach (var e in edits) edit.Replace(e.Start, e.Length, e.Text);
                edit.Apply();
            }
        }
    }
}
