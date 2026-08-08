using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalComents.Models;
using LocalComents.Services;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Utilities;

namespace LocalComents.Editor
{
    /// <summary>
    /// Draws the comment text at the end of the annotated line, the way inline hints do.
    /// This is what makes an annotation obvious without hovering.
    /// </summary>
    internal sealed class InlineCommentAdornment
    {
        private const string LayerName = "LocalComentsInline";
        private const int MaxInlineLength = 120;

        private readonly IWpfTextView _view;
        private readonly IAdornmentLayer _layer;
        private readonly IClassificationFormatMap _formatMap;
        private readonly IClassificationType? _commentClassification;

        public InlineCommentAdornment(
            IWpfTextView view,
            IClassificationFormatMapService formatMapService,
            IClassificationTypeRegistryService classificationRegistry)
        {
            _view = view;
            _layer = view.GetAdornmentLayer(LayerName);
            _formatMap = formatMapService.GetClassificationFormatMap(view);
            _commentClassification = classificationRegistry.GetClassificationType("comment");

            _view.LayoutChanged += OnLayoutChanged;
            _view.Closed += OnClosed;
            _formatMap.ClassificationFormatMappingChanged += OnFormatMappingChanged;
            CommentStore.Instance.CommentsChanged += OnCommentsChanged;
        }

        private void OnClosed(object sender, EventArgs e)
        {
            _view.LayoutChanged -= OnLayoutChanged;
            _view.Closed -= OnClosed;
            _formatMap.ClassificationFormatMappingChanged -= OnFormatMappingChanged;
            CommentStore.Instance.CommentsChanged -= OnCommentsChanged;
        }

        private void OnFormatMappingChanged(object sender, EventArgs e) => RedrawAll();

        private void OnCommentsChanged(object sender, CommentsChangedEventArgs e)
        {
            var path = _view.TextBuffer.GetFilePath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (e.FilePath != null && !string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // The store can raise this from its file watcher thread; redrawing is
            // fire-and-forget because nothing waits on the adornment being up to date.
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RedrawAll();
            }).FileAndForget("LocalComents/Adornment/Redraw");
#pragma warning restore VSSDK007
        }

        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            foreach (var line in e.NewOrReformattedLines)
            {
                DrawLine(line);
            }
        }

        private void RedrawAll()
        {
            if (_view.IsClosed || _view.InLayout)
            {
                return;
            }

            _layer.RemoveAllAdornments();

            foreach (var line in _view.TextViewLines)
            {
                DrawLine(line);
            }
        }

        private void DrawLine(ITextViewLine line)
        {
            if (!LocalComentsSettings.ShowInlineText)
            {
                return;
            }

            var snapshot = _view.TextSnapshot;
            var comments = CommentsOnLine(snapshot, line);
            if (comments.Count == 0)
            {
                return;
            }

            var element = BuildElement(comments);
            element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            Canvas.SetLeft(element, line.TextRight + 24);
            Canvas.SetTop(element, line.TextTop + Math.Max(0, (line.Height - element.DesiredSize.Height) / 2));

            var span = new SnapshotSpan(line.Start, line.End);
            _layer.AddAdornment(AdornmentPositioningBehavior.TextRelative, span, LayerName, element, null);
        }

        private IReadOnlyList<LocalComment> CommentsOnLine(ITextSnapshot snapshot, ITextViewLine line)
        {
            var path = _view.TextBuffer.GetFilePath();
            if (string.IsNullOrEmpty(path))
            {
                return Array.Empty<LocalComment>();
            }

            var lineNumber = snapshot.GetLineNumberFromPosition(line.Start);

            return CommentStore.Instance.GetComments(path)
                .Where(c => c.Range != null
                            && c.Range.StartLine == lineNumber
                            && CommentSpanMapper.ShouldRender(snapshot, c))
                .ToArray();
        }

        private UIElement BuildElement(IReadOnlyList<LocalComment> comments)
        {
            var text = string.Join("  •  ", comments.Select(c => Flatten(c.Text)));
            if (text.Length > MaxInlineLength)
            {
                text = text.Substring(0, MaxInlineLength) + "…";
            }

            var block = new TextBlock
            {
                Text = "💬 " + text,
                FontSize = Math.Max(8, _view.FormattedLineSource.DefaultTextProperties.FontRenderingEmSize - 1),
                FontFamily = _view.FormattedLineSource.DefaultTextProperties.Typeface.FontFamily,
                FontStyle = FontStyles.Italic,
                Foreground = GetInlineBrush(),
                ToolTip = string.Join(Environment.NewLine + Environment.NewLine, comments.Select(c => c.Text)),
                IsHitTestVisible = true,
            };

            return new Border
            {
                Child = block,
                Padding = new Thickness(6, 0, 6, 0),
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromArgb(28, 0xF5, 0xD1, 0x76)),
            };
        }

        /// <summary>
        /// Reuses the editor's own "comment" colour so the annotation blends with the current
        /// theme instead of hard-coding a light/dark value.
        /// </summary>
        private Brush GetInlineBrush()
        {
            if (_commentClassification != null)
            {
                var properties = _formatMap.GetTextProperties(_commentClassification);
                if (!properties.ForegroundBrushEmpty && properties.ForegroundBrush != null)
                {
                    var brush = properties.ForegroundBrush.Clone();
                    brush.Opacity = 0.9;
                    brush.Freeze();
                    return brush;
                }
            }

            return new SolidColorBrush(Color.FromRgb(0x9A, 0x9A, 0x9A));
        }

        private static string Flatten(string value)
            => string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class InlineCommentAdornmentProvider : IWpfTextViewCreationListener
    {
#pragma warning disable CS0649 // Assigned by MEF.
        [Export(typeof(AdornmentLayerDefinition))]
        [Name("LocalComentsInline")]
        [Order(After = PredefinedAdornmentLayers.Text)]
        [TextViewRole(PredefinedTextViewRoles.Document)]
        internal AdornmentLayerDefinition? InlineLayer;
#pragma warning restore CS0649

        [Import]
        internal IClassificationFormatMapService FormatMapService = null!;

        [Import]
        internal IClassificationTypeRegistryService ClassificationRegistry = null!;

        public void TextViewCreated(IWpfTextView textView)
        {
            _ = new InlineCommentAdornment(textView, FormatMapService, ClassificationRegistry);
        }
    }
}
