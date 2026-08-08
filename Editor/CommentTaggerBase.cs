using System;
using System.Collections.Generic;
using LocalComents.Models;
using LocalComents.Services;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;

namespace LocalComents.Editor
{
    /// <summary>
    /// Shared plumbing for taggers driven by <see cref="CommentStore"/>: it resolves the
    /// file behind the buffer and re-raises <see cref="TagsChanged"/> whenever the comments
    /// for that file are added, edited, removed or reloaded from disk.
    /// </summary>
    internal abstract class CommentTaggerBase<TTag> : ITagger<TTag>, IDisposable
        where TTag : ITag
    {
        private bool _disposed;

        protected CommentTaggerBase(ITextBuffer buffer)
        {
            Buffer = buffer;
            CommentStore.Instance.CommentsChanged += OnCommentsChanged;
        }

        public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

        protected ITextBuffer Buffer { get; }

        public abstract IEnumerable<ITagSpan<TTag>> GetTags(NormalizedSnapshotSpanCollection spans);

        protected IReadOnlyList<LocalComment> GetCommentsForBuffer()
        {
            var path = Buffer.GetFilePath();
            return string.IsNullOrEmpty(path)
                ? Array.Empty<LocalComment>()
                : CommentStore.Instance.GetComments(path);
        }

        private void OnCommentsChanged(object sender, CommentsChangedEventArgs e)
        {
            var path = Buffer.GetFilePath();
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (e.FilePath != null && !string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var snapshot = Buffer.CurrentSnapshot;
            TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snapshot, 0, snapshot.Length)));
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            CommentStore.Instance.CommentsChanged -= OnCommentsChanged;
        }
    }
}
