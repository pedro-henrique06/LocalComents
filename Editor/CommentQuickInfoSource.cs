using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using LocalComents.Services;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Utilities;

namespace LocalComents.Editor
{
    /// <summary>Shows the comment text when hovering over annotated code.</summary>
    internal sealed class CommentQuickInfoSource : IAsyncQuickInfoSource
    {
        private readonly ITextBuffer _buffer;

        public CommentQuickInfoSource(ITextBuffer buffer) => _buffer = buffer;

        public Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
        {
            var snapshot = _buffer.CurrentSnapshot;
            var triggerPoint = session.GetTriggerPoint(snapshot);
            if (triggerPoint == null)
            {
                return Task.FromResult<QuickInfoItem?>(null);
            }

            var position = triggerPoint.Value;
            var elements = new List<object>();
            SnapshotSpan? applicable = null;

            foreach (var comment in CommentStore.Instance.GetComments(_buffer.GetFilePath()))
            {
                if (!CommentSpanMapper.ShouldRender(snapshot, comment))
                {
                    continue;
                }

                if (!CommentSpanMapper.TryGetSpan(snapshot, comment, out var span) || !span.Contains(position))
                {
                    continue;
                }

                applicable ??= span;

                var header = comment.Timestamp > 0
                    ? $"Local comment — {comment.CreatedAtLocal:g}"
                    : "Local comment";

                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.SymbolDefinition, header)));
                elements.Add(new ClassifiedTextElement(
                    new ClassifiedTextRun(PredefinedClassificationTypeNames.NaturalLanguage, comment.Text)));

                if (CommentSpanMapper.IsStale(snapshot, comment))
                {
                    elements.Add(new ClassifiedTextElement(
                        new ClassifiedTextRun(PredefinedClassificationTypeNames.ExcludedCode, "(the code below this comment has changed)")));
                }
            }

            if (applicable == null || elements.Count == 0)
            {
                return Task.FromResult<QuickInfoItem?>(null);
            }

            var trackingSpan = snapshot.CreateTrackingSpan(applicable.Value, SpanTrackingMode.EdgeInclusive);
            var container = new ContainerElement(ContainerElementStyle.Stacked, elements);
            return Task.FromResult<QuickInfoItem?>(new QuickInfoItem(trackingSpan, container));
        }

        public void Dispose()
        {
        }
    }

    [Export(typeof(IAsyncQuickInfoSourceProvider))]
    [Name("Local Comments Quick Info")]
    [ContentType("text")]
    [Order(Before = "default")]
    internal sealed class CommentQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
    {
        public IAsyncQuickInfoSource? TryCreateQuickInfoSource(ITextBuffer textBuffer)
            => textBuffer.Properties.GetOrCreateSingletonProperty(() => new CommentQuickInfoSource(textBuffer));
    }
}
