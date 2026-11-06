using System;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using SqlTools.Completion;
using SqlTools.Ssms.Completion;
using SqlTools.Ssms.Integration;

namespace SqlTools.Ssms.Navigation
{
    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("QueryPilotQuickInfo")]
    [ContentType(SqlContentType.Name)]
    [Order]
    internal sealed class QuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
            textBuffer.Properties.GetOrCreateSingletonProperty(() => new QuickInfoSource(textBuffer));
    }

    /// <summary>Mouse sobre tabela/view/procedure/coluna: colunas com tipo, parâmetros, PK.</summary>
    internal sealed class QuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;

        public QuickInfoSource(ITextBuffer buffer) { _buffer = buffer; }

        public async Task<QuickInfoItem> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            try
            {
                var settings = SqlToolsPackage.Instance?.Settings?.Current;
                if (settings == null || !settings.CompletionEnabled) return null;
                var snapshot = _buffer.CurrentSnapshot;
                var point = session.GetTriggerPoint(snapshot);
                if (point == null) return null;

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                var conn = SsmsBridge.GetActiveWindowEditorConnection();
                if (conn == null) return null;
                var md = await MetadataService.Instance.GetAsync(conn, TimeSpan.FromSeconds(1), cancellationToken);
                var sym = SymbolResolver.At(snapshot.GetText(), point.Value.Position, md);
                var text = SymbolResolver.Describe(sym);
                if (text == null) return null;
                var span = snapshot.CreateTrackingSpan(sym.Start, sym.Length, SpanTrackingMode.EdgeInclusive);
                return new QuickInfoItem(span, text);
            }
            catch (Exception ex)
            {
                if (ex is OperationCanceledException) throw;
                SsmsBridge.WarnOnce("quickinfo", "Falha na dica do mouse.", ex);
                return null;
            }
        }

        public void Dispose() { }
    }
}
