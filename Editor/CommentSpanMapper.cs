using System;
using LocalComents.Models;
using LocalComents.Services;
using Microsoft.VisualStudio.Text;

namespace LocalComents.Editor
{
    /// <summary>
    /// Translates the editor-agnostic, zero-based ranges stored on disk into spans of a
    /// concrete <see cref="ITextSnapshot"/>.
    /// </summary>
    internal static class CommentSpanMapper
    {
        public static bool TryGetSpan(ITextSnapshot snapshot, LocalComment comment, out SnapshotSpan span)
        {
            span = default;

            var range = comment.Range;
            if (range == null || range.StartLine < 0 || range.StartLine >= snapshot.LineCount)
            {
                return false;
            }

            var startLine = snapshot.GetLineFromLineNumber(range.StartLine);
            var endLineNumber = Math.Max(range.StartLine, Math.Min(range.EndLine, snapshot.LineCount - 1));
            var endLine = snapshot.GetLineFromLineNumber(endLineNumber);

            var start = startLine.Start + Math.Min(Math.Max(range.StartCharacter, 0), startLine.Length);

            int end;
            if (range.IsWholeLine)
            {
                end = endLine.End;
            }
            else
            {
                var offset = (int)Math.Min(Math.Max(range.EndCharacter, 0), endLine.Length);
                end = endLine.Start + offset;
            }

            if (end < start)
            {
                end = start;
            }

            // A zero-width span would be invisible; fall back to the whole line.
            if (end == start)
            {
                start = startLine.Start;
                end = startLine.End;
            }

            span = new SnapshotSpan(snapshot, Span.FromBounds(start, end));
            return true;
        }

        /// <summary>
        /// A comment is considered stale when the code it was anchored to no longer matches.
        /// </summary>
        public static bool IsStale(ITextSnapshot snapshot, LocalComment comment)
        {
            var anchor = comment.Range?.SelectedText;
            if (string.IsNullOrWhiteSpace(anchor))
            {
                return false;
            }

            if (!TryGetSpan(snapshot, comment, out var span))
            {
                return true;
            }

            var current = span.GetText();
            return current.IndexOf(anchor!.Trim(), StringComparison.Ordinal) < 0
                && !string.Equals(current.Trim(), anchor.Trim(), StringComparison.Ordinal);
        }

        public static bool ShouldRender(ITextSnapshot snapshot, LocalComment comment)
        {
            return !LocalComentsSettings.HideStaleComments || !IsStale(snapshot, comment);
        }
    }
}
