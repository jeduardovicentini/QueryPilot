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
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Completion
{
    /// <summary>
    /// Tab com o cursor no * (antes ou depois dele) e sem lista aberta: troca o * pelas colunas com alias
    /// (SELECT * FROM oCarrinho → SELECT oc.Col1, oc.Col2 FROM oCarrinho oc). Fora disso, o Tab segue normal.
    /// </summary>
    [Export(typeof(ICommandHandler))]
    [ContentType(SqlContentType.Name)]
    [Name("QueryPilotStarTab")]
    internal sealed class StarTabCommandHandler : ICommandHandler<TabKeyCommandArgs>
    {
        private static readonly TimeSpan MetadataWait = TimeSpan.FromSeconds(3);

        [Import]
        internal IAsyncCompletionBroker Broker { get; set; }

        public string DisplayName => ProductInfo.Name + ": expandir *";

        public CommandState GetCommandState(TabKeyCommandArgs args) => CommandState.Unspecified;

        public bool ExecuteCommand(TabKeyCommandArgs args, CommandExecutionContext executionContext)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var settings = SqlToolsPackage.Instance?.Settings?.Current;
                if (settings == null || !settings.CompletionEnabled) return false;
                var view = args.TextView;
                if (Broker != null && Broker.IsCompletionActive(view)) return false;
                if (!view.Selection.IsEmpty) return false;

                var buffer = args.SubjectBuffer;
                var point = view.Caret.Position.Point.GetPoint(buffer, PositionAffinity.Predecessor);
                if (point == null) return false;
                var snapshot = buffer.CurrentSnapshot;
                int caret = point.Value.Position;
                bool atStar = (caret < snapshot.Length && snapshot[caret] == '*') || (caret > 0 && snapshot[caret - 1] == '*');
                if (!atStar) return false;

                var conn = SsmsBridge.GetActiveWindowEditorConnection();
                if (conn == null) return false;
                var md = ThreadHelper.JoinableTaskFactory.Run(() => MetadataService.Instance.GetAsync(conn, MetadataWait, CancellationToken.None));
                if (md == null) return false;

                var edits = CompletionEngine.ExpandStar(snapshot.GetText(), caret, md);
                if (edits == null) return false;

                // cursor no fim da lista de colunas: posição do * + texto novo + deslocamento das edições antes dele
                var star = edits.First(e => e.IsColumns);
                int shift = edits.Where(e => e.Start < star.Start).Sum(e => e.Text.Length - e.Length);
                int newCaret = star.Start + shift + star.Text.Length;

                using (var edit = buffer.CreateEdit())
                {
                    foreach (var e in edits) edit.Replace(e.Start, e.Length, e.Text);
                    edit.Apply();
                }
                view.Caret.MoveTo(new SnapshotPoint(buffer.CurrentSnapshot, Math.Min(newCaret, buffer.CurrentSnapshot.Length)));
                return true;
            }
            catch (Exception ex)
            {
                SsmsBridge.WarnOnce("star-tab", "Falha ao expandir o *.", ex);
                return false;
            }
        }
    }
}
