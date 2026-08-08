using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows.Media;
using LocalComents.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace LocalComents.Editor
{
    /// <summary>Highlights the code covered by a local comment.</summary>
    internal sealed class CommentHighlightTagger : CommentTaggerBase<TextMarkerTag>
    {
        private static readonly TextMarkerTag Tag = new TextMarkerTag(LocalCommentMarkerFormat.Name);

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
                    yield return new TagSpan<TextMarkerTag>(span, Tag);
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

    [Export(typeof(EditorFormatDefinition))]
    [Name(Name)]
    [UserVisible(true)]
    internal sealed class LocalCommentMarkerFormat : MarkerFormatDefinition
    {
        public const string Name = "LocalComents.CommentMarker";

        public LocalCommentMarkerFormat()
        {
            BackgroundColor = Color.FromRgb(0xF5, 0xD1, 0x76);
            ForegroundColor = Color.FromRgb(0xC9, 0x94, 0x18);
            DisplayName = "Local Comments Highlight";
            ZOrder = 5;
        }
    }
}
