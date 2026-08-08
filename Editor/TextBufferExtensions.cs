using Microsoft.VisualStudio.Text;

namespace LocalComents.Editor
{
    internal static class TextBufferExtensions
    {
        /// <summary>Returns the path of the file backing the buffer, or <c>null</c> for unsaved buffers.</summary>
        public static string? GetFilePath(this ITextBuffer buffer)
        {
            if (buffer.Properties.TryGetProperty(typeof(ITextDocument), out ITextDocument document))
            {
                return document?.FilePath;
            }

            return null;
        }
    }
}
