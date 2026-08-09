using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalComents.Models;
using LocalComents.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace LocalComents.Editor
{
    internal sealed class CommentGlyphTag : IGlyphTag
    {
        public CommentGlyphTag(IReadOnlyList<LocalComment> comments) => Comments = comments;

        public IReadOnlyList<LocalComment> Comments { get; }
    }

    internal sealed class CommentGlyphTagger : CommentTaggerBase<CommentGlyphTag>
    {
        public CommentGlyphTagger(ITextBuffer buffer)
            : base(buffer)
        {
        }

        public override IEnumerable<ITagSpan<CommentGlyphTag>> GetTags(NormalizedSnapshotSpanCollection spans)
        {
            if (spans.Count == 0 || !LocalComentsSettings.ShowGlyph)
            {
                yield break;
            }

            var snapshot = spans[0].Snapshot;
            var comments = GetCommentsForBuffer()
                .Where(c => CommentSpanMapper.ShouldRender(snapshot, c))
                .GroupBy(c => c.Range.StartLine);

            foreach (var group in comments)
            {
                if (group.Key < 0 || group.Key >= snapshot.LineCount)
                {
                    continue;
                }

                var line = snapshot.GetLineFromLineNumber(group.Key);
                var span = new SnapshotSpan(line.Start, 0);
                if (!spans.IntersectsWith(new NormalizedSnapshotSpanCollection(line.Extent)))
                {
                    continue;
                }

                yield return new TagSpan<CommentGlyphTag>(span, new CommentGlyphTag(group.ToArray()));
            }
        }
    }

    [Export(typeof(ITaggerProvider))]
    [ContentType("text")]
    [TagType(typeof(CommentGlyphTag))]
    internal sealed class CommentGlyphTaggerProvider : ITaggerProvider
    {
        public ITagger<T>? CreateTagger<T>(ITextBuffer buffer)
            where T : ITag
        {
            return buffer.Properties.GetOrCreateSingletonProperty(
                () => new CommentGlyphTagger(buffer)) as ITagger<T>;
        }
    }

    [Export(typeof(IGlyphFactoryProvider))]
    [Name("LocalComentsGlyph")]
    [Order(After = "VsTextMarker")]
    [ContentType("text")]
    [TagType(typeof(CommentGlyphTag))]
    internal sealed class CommentGlyphFactoryProvider : IGlyphFactoryProvider
    {
        public IGlyphFactory GetGlyphFactory(IWpfTextView view, IWpfTextViewMargin margin)
            => new CommentGlyphFactory();
    }

    internal sealed class CommentGlyphFactory : IGlyphFactory
    {
        private const double GlyphSize = 12.0;

        public UIElement? GenerateGlyph(IWpfTextViewLine line, IGlyphTag tag)
        {
            if (tag is not CommentGlyphTag commentTag)
            {
                return null;
            }

            // With several comments on one line the margin only has room for one bubble, so it
            // takes the first one's colour.
            var palette = CommentPalette.Resolve(commentTag.Comments.Count > 0 ? commentTag.Comments[0].Color : null);

            var bubble = new Border
            {
                Width = GlyphSize,
                Height = GlyphSize,
                CornerRadius = new CornerRadius(3, 3, 3, 0),
                Background = new SolidColorBrush(palette.Highlight),
                BorderBrush = new SolidColorBrush(palette.Border),
                BorderThickness = new Thickness(1),
                ToolTip = BuildToolTip(commentTag.Comments),
            };

            return bubble;
        }

        private static string BuildToolTip(IReadOnlyList<LocalComment> comments)
        {
            return string.Join("\n\n", comments.Select(c => c.Text));
        }
    }
}
