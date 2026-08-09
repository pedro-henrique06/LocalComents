using System.Collections.Generic;
using System.ComponentModel.Composition;
using LocalComents.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace LocalComents.Editor
{
    /// <summary>Highlights the code covered by a local comment, in the comment's own colour.</summary>
    internal sealed class CommentHighlightTagger : CommentTaggerBase<TextMarkerTag>
    {
        public CommentHighlightTagger(ITextBuffer buffer)
            : base(buffer)
        {
        }

        public override IEnumerable<ITagSpan<TextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0 || !LocalComentsSettings.HighlightRange)
            {
                yield break;
            }

            var snapshot = spans[0].Snapshot;
            foreach (var comment in GetCommentsForBuffer())
            {
                if (!CommentSpanMapper.ShouldRender(snapshot, comment))
                {
                    continue;
                }

                if (CommentSpanMapper.TryGetSpan(snapshot, comment, out var span) && spans.IntersectsWith(span))
                {
                    yield return new TagSpan<TextMarkerTag>(span, CommentPalette.Resolve(comment.Color).MarkerTag);
                }
            }
        }
    }

    [Export(typeof(IViewTaggerProvider))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    [TagType(typeof(TextMarkerTag))]
    internal sealed class CommentHighlightTaggerProvider : IViewTaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextView textView, ITextBuffer buffer)
            where T : ITag
        {
            if (textView.TextBuffer != buffer)
            {
                return null;
            }

            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new CommentHighlightTagger(buffer)) as ITagger<T>;
        }
    }
}
