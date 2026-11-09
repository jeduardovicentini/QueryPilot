using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using SqlTools.Completion;
using SqlTools.Infra;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Completion
{
    /// <summary>Content type do editor de T-SQL do SSMS 22 (RadLangSvc).</summary>
    internal static class SqlContentType
    {
        public const string Name = "SQL";
    }

    [Export(typeof(IAsyncCompletionSourceProvider))]
    [Name(ProductInfo.Name + " completion")]
    [ContentType(SqlContentType.Name)]
    internal sealed class CompletionSourceProvider : IAsyncCompletionSourceProvider
    {
        public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCompletionSource());
    }

    /// <summary>Ao abrir/focar um editor SQL, já começa a carregar os metadados do banco dele.</summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType(SqlContentType.Name)]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal sealed class MetadataPreloader : IWpfTextViewCreationListener
    {
        public void TextViewCreated(IWpfTextView textView)
        {
            // o language service nativo relê as preferências ao abrir o editor; reaplica depois dele
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await Task.Delay(1500);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var settings = SqlToolsPackage.Instance?.Settings;
                if (settings != null) NativeIntelliSense.Apply(settings);
            }).FileAndForget(ProductInfo.Name + "/NativeIntelliSense");

            Preload(TimeSpan.FromSeconds(2));
            textView.GotAggregateFocus += (s, e) => Preload(TimeSpan.Zero);
        }

        public static void Preload(TimeSpan delay)
        {
            if (SqlToolsPackage.Instance?.Settings?.Current.CompletionEnabled != true) return;
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                if (delay > TimeSpan.Zero) await Task.Delay(delay);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var conn = SsmsBridge.GetActiveWindowEditorConnection();
                if (conn != null) _ = MetadataService.Instance.GetAsync(conn, TimeSpan.Zero, CancellationToken.None);
            }).FileAndForget(ProductInfo.Name + "/MetadataPreload");
        }
    }

    internal sealed class SqlCompletionSource : IAsyncCompletionSource
    {
        private static readonly TimeSpan MetadataWait = TimeSpan.FromSeconds(3);

        /// <summary>Contextos que abrem a lista sozinhos depois de um espaço (FROM ␣, JOIN ␣, EXEC ␣...).</summary>
        private static readonly HashSet<ContextKind> AfterSpace = new HashSet<ContextKind>
        {
            ContextKind.ObjectName, ContextKind.Procedure, ContextKind.Database, ContextKind.JoinCondition, ContextKind.AfterInsertTarget,
            ContextKind.ProcParameters
        };

        public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
        {
            try
            {
                var settings = SqlToolsPackage.Instance?.Settings;
                if (settings == null || !settings.Current.CompletionEnabled) return CompletionStartData.DoesNotParticipateInCompletion;

                var snapshot = triggerLocation.Snapshot;
                var caret = triggerLocation.Position;
                var ctx = SqlContextAnalyzer.Analyze(snapshot.GetText(), caret);
                if (ctx.Kind == ContextKind.None || !Accepts(trigger, triggerLocation, ctx)) return CompletionStartData.DoesNotParticipateInCompletion;

                var span = ctx.Kind == ContextKind.StarExpansion
                    ? new SnapshotSpan(snapshot, ctx.StarStart, ctx.StarLength)
                    : new SnapshotSpan(snapshot, ctx.ReplaceStart, Math.Max(ctx.ReplaceLength, caret - ctx.ReplaceStart));
                return new CompletionStartData(CompletionParticipation.ProvidesItems, span);
            }
            catch (Exception ex)
            {
                SsmsBridge.WarnOnce("completion-init", "Falha ao iniciar o autocomplete.", ex);
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }

        /// <summary>Quando a lista abre sozinha enquanto se digita (Ctrl+Espaço sempre abre).</summary>
        private static bool Accepts(CompletionTrigger trigger, SnapshotPoint at, SqlCompletionContext ctx)
        {
            switch (trigger.Reason)
            {
                case CompletionTriggerReason.Invoke:
                case CompletionTriggerReason.InvokeAndCommitIfUnique:
                    return true;
                case CompletionTriggerReason.Insertion:
                    break;
                default:
                    return false;
            }

            var c = trigger.Character;
            if (ctx.Kind == ContextKind.StarExpansion || ctx.Kind == ContextKind.AfterTableRef) return false;
            if (c == '.') return ctx.Kind == ContextKind.Member;
            if (c == '@') return ctx.Kind == ContextKind.Variable;
            if (c == ' ')
                return AfterSpace.Contains(ctx.Kind) ||
                       ctx.Kind == ContextKind.Expression && SqlContextAnalyzer.AfterConditionKeyword(at.Snapshot.GetText(0, at.Position), at.Position);
            if (c == '(' || c == ',') return ctx.Kind == ContextKind.InsertColumns || (c == ',' && (ctx.Kind == ContextKind.ObjectName || ctx.Kind == ContextKind.ProcParameters));
            if (char.IsLetter(c) || c == '_' || c == '#')
            {
                // só no começo da palavra
                var pos = at.Position - 2;
                return pos < 0 || !Sql.SqlTokenizer.IsIdentChar(at.Snapshot[pos]) && at.Snapshot[pos] != '[';
            }
            return false;
        }

        public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
        {
            try
            {
                var snapshot = triggerLocation.Snapshot;
                var ctx = SqlContextAnalyzer.Analyze(snapshot.GetText(), triggerLocation.Position);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                var pkg = SqlToolsPackage.Instance;
                var conn = SsmsBridge.GetActiveWindowEditorConnection();
                var options = new CompletionOptions { AutoAlias = pkg?.Settings.Current.CompletionAutoAlias ?? true, UserSnippets = UserSnippetStore.Current };
                await TaskScheduler.Default;

                var md = await MetadataService.Instance.GetAsync(conn, MetadataWait, token).ConfigureAwait(false);
                var suggestions = CompletionEngine.GetSuggestions(ctx, md, options);
                var items = suggestions.Select(ToItem).ToImmutableArray();
                return new CompletionContext(items);
            }
            catch (OperationCanceledException)
            {
                return CompletionContext.Empty;
            }
            catch (Exception ex)
            {
                SsmsBridge.WarnOnce("completion-items", "Falha ao montar a lista do autocomplete.", ex);
                return CompletionContext.Empty;
            }
        }

        private CompletionItem ToItem(Suggestion s)
        {
            var item = new CompletionItem(
                displayText: s.DisplayText,
                source: this,
                icon: Icon(s.Kind),
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: s.Detail ?? "",
                insertText: s.InsertText,
                sortText: s.Priority.ToString("D3") + " " + s.DisplayText,
                filterText: s.FilterText,
                attributeIcons: ImmutableArray<ImageElement>.Empty);
            item.Properties.AddProperty(typeof(Suggestion), s);
            return item;
        }

        public Task<object> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
            Task.FromResult<object>(item.Properties.TryGetProperty(typeof(Suggestion), out Suggestion s) ? s.Description : null);

        private static readonly Dictionary<SuggestionKind, ImageElement> Icons = new Dictionary<SuggestionKind, ImageElement>();

        private static ImageElement Icon(SuggestionKind kind)
        {
            lock (Icons)
            {
                if (Icons.TryGetValue(kind, out var img)) return img;
                ImageMoniker m;
                switch (kind)
                {
                    case SuggestionKind.Table: m = KnownMonikers.Table; break;
                    case SuggestionKind.View: m = KnownMonikers.View; break;
                    case SuggestionKind.Cte: m = KnownMonikers.View; break;
                    case SuggestionKind.TableFunction: m = KnownMonikers.TableFunction; break;
                    case SuggestionKind.ScalarFunction: m = KnownMonikers.ScalarFunction; break;
                    case SuggestionKind.Function: m = KnownMonikers.MethodPublic; break;
                    case SuggestionKind.Procedure: m = KnownMonikers.StoredProcedure; break;
                    case SuggestionKind.Synonym: m = KnownMonikers.Synonym; break;
                    case SuggestionKind.Column: m = KnownMonikers.Column; break;
                    case SuggestionKind.Variable: m = KnownMonikers.LocalVariable; break;
                    case SuggestionKind.Database: m = KnownMonikers.Database; break;
                    case SuggestionKind.Schema: m = KnownMonikers.Namespace; break;
                    case SuggestionKind.Alias: m = KnownMonikers.LocalVariable; break;
                    case SuggestionKind.Join: m = KnownMonikers.Relationship; break;
                    case SuggestionKind.JoinCondition: m = KnownMonikers.Relationship; break;
                    case SuggestionKind.ColumnList: m = KnownMonikers.ColumnGroup; break;
                    case SuggestionKind.Snippet: m = KnownMonikers.Snippet; break;
                    case SuggestionKind.Parameter: m = KnownMonikers.Parameter; break;
                    default: m = KnownMonikers.IntellisenseKeyword; break;
                }
                img = new ImageElement(new ImageId(m.Guid, m.Id));
                Icons[kind] = img;
                return img;
            }
        }
    }

    [Export(typeof(IAsyncCompletionCommitManagerProvider))]
    [Name(ProductInfo.Name + " commit")]
    [ContentType(SqlContentType.Name)]
    internal sealed class CommitManagerProvider : IAsyncCompletionCommitManagerProvider
    {
        public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
            textView.Properties.GetOrCreateSingletonProperty(() => new SqlCommitManager());
    }

    /// <summary>
    /// Tab e Enter confirmam sempre. Qualquer outro caractere que não faz parte de nome fecha a lista:
    /// <c>, ( ) ; =</c> confirmam se o digitado é começo do item (Na, → Name,); espaço, ponto e o resto só confirmam
    /// quando o digitado é exatamente o item (select␣ → SELECT␣). Assim um alias ou valor digitado nunca é trocado.
    /// </summary>
    internal sealed class SqlCommitManager : IAsyncCompletionCommitManager
    {
        private static readonly char[] PrefixCommit = { ',', '(', ')', ';', '=' };
        private static readonly char[] Chars = { ' ', '.', ',', '(', ')', ';', '=', '+', '-', '*', '/', '<', '>', '!', '%', '&', '|', '^', '~', '\'', '"', ']', '{', '}', ':' };

        public IEnumerable<char> PotentialCommitCharacters => Chars;

        public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) =>
            Array.IndexOf(Chars, typedChar) >= 0;

        public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
        {
            item.Properties.TryGetProperty(typeof(Suggestion), out Suggestion s);
            if (typedChar == '\0' || typedChar == '\t' || typedChar == '\n' || typedChar == '\r')
            {
                if (s != null && s.Kind == SuggestionKind.Snippet) FixSnippetAfterCommit(session, buffer, s);
                return CommitResult.Unhandled;
            }

            var typed = session.ApplicableToSpan.GetText(buffer.CurrentSnapshot) ?? "";
            var multiWord = s != null && (s.Kind == SuggestionKind.Join || s.Kind == SuggestionKind.JoinCondition || s.Kind == SuggestionKind.ColumnList);
            var filter = item.FilterText ?? item.DisplayText;
            var match = typed.Length > 0 && !multiWord && (Array.IndexOf(PrefixCommit, typedChar) >= 0
                ? filter.StartsWith(typed, StringComparison.OrdinalIgnoreCase)
                : string.Equals(typed, filter, StringComparison.OrdinalIgnoreCase));

            // não confirmou: fecha a lista e o caractere entra normalmente
            return match ? CommitResult.Unhandled : new CommitResult(true, CommitBehavior.None);
        }

        /// <summary>
        /// Snippet de várias linhas ou com $CURSOR$: o SSMS insere o texto pelo caminho padrão (mesmo quando o
        /// commit diz que tratou); logo depois, indentamos as linhas seguintes como a linha atual e movemos o cursor.
        /// </summary>
        private static void FixSnippetAfterCommit(IAsyncCompletionSession session, ITextBuffer buffer, Suggestion s)
        {
            if (s.CursorOffset == null && s.InsertText.IndexOf('\n') < 0) return; // uma linha: o padrão já serve
            var start = buffer.CurrentSnapshot.CreateTrackingPoint(session.ApplicableToSpan.GetStartPoint(buffer.CurrentSnapshot), PointTrackingMode.Negative);
            EventHandler<CompletionItemEventArgs> handler = null;
            handler = (sender, e) =>
            {
                session.ItemCommitted -= handler;
                try
                {
                    var snapshot = buffer.CurrentSnapshot;
                    var pos = start.GetPosition(snapshot);
                    if (pos + s.InsertText.Length > snapshot.Length || snapshot.GetText(pos, s.InsertText.Length) != s.InsertText) return;
                    var line = snapshot.GetLineFromPosition(pos);
                    var before = snapshot.GetText(line.Start, pos - line.Start);
                    var indent = before.Substring(0, before.Length - before.TrimStart(' ', '\t').Length);
                    var newLine = line.LineBreakLength > 0 ? line.GetLineBreakText()
                        : snapshot.LineCount > 1 ? snapshot.GetLineFromLineNumber(0).GetLineBreakText() : "\r\n";
                    var text = Sql.SqlSnippets.Expand(s.InsertText, s.CursorOffset, indent, newLine, out var caret);
                    var after = text == s.InsertText ? snapshot : buffer.Replace(new Span(pos, s.InsertText.Length), text);
                    session.TextView.Caret.MoveTo(new SnapshotPoint(after, pos + caret));
                }
                catch (Exception ex)
                {
                    Log.Warn("Falha ao ajustar o snippet " + s.DisplayText + ".", ex);
                }
            };
            session.ItemCommitted += handler;
        }
    }
}
