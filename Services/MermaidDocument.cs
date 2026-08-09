using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace LocalComents.Services
{
    /// <summary>
    /// Reads the Markdown the agent produced through the MCP server's <c>write_documentation</c>
    /// tool and pulls the Mermaid diagrams out of it.
    /// <para>
    /// The extension never generates diagrams itself — it renders what the agent wrote. That is
    /// why this watches a file instead of analysing code: the source of truth is the document on
    /// disk, and rerunning the <c>generate_documentation</c> prompt is what refreshes it.
    /// </para>
    /// </summary>
    internal static class MermaidDocument
    {
        /// <summary>Default name written by <c>write_documentation</c>.</summary>
        public const string DefaultFileName = "DOCUMENTATION.md";

        /// <summary>The folder documents are written to — the one holding the comments file.</summary>
        public static string? ResolveFolder()
        {
            var storagePath = CommentStore.Instance.StoragePath;
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                return null;
            }

            var folder = Path.GetDirectoryName(storagePath);
            return string.IsNullOrEmpty(folder) ? null : folder;
        }

        /// <summary>
        /// The document to render, or <c>null</c> when the folder holds none.
        /// <para>
        /// <c>DefaultFileName</c> wins when it is there, but the agent chooses the file name and
        /// does not always take the default — so any Markdown in the folder carrying a diagram
        /// counts, most recently written first. Requiring a diagram is what keeps an unrelated
        /// README from being picked up.
        /// </para>
        /// </summary>
        public static string? FindDocument(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return null;
            }

            var preferred = Path.Combine(folder, DefaultFileName);
            if (ContainsDiagram(preferred))
            {
                return preferred;
            }

            try
            {
                return Directory.GetFiles(folder, "*.md", SearchOption.TopDirectoryOnly)
                    .Select(path => new FileInfo(path))
                    .OrderByDescending(file => file.LastWriteTimeUtc)
                    .Select(file => file.FullName)
                    .FirstOrDefault(ContainsDiagram);
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Could not scan '{folder}' for documents: {ex.Message}");
                return null;
            }
        }

        private static bool ContainsDiagram(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return false;
                }

                // Generated documentation is prose; anything this large is not it, and reading it
                // on every refresh would be the expensive part of opening the window.
                if (new FileInfo(path).Length > 2 * 1024 * 1024)
                {
                    return false;
                }

                return ExtractDiagrams(File.ReadAllText(path)).Count > 0;
            }
            catch (IOException)
            {
                // Being written right now; the watcher will bring us back.
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// Extracts the contents of every <c>```mermaid</c> fenced block, in document order.
        /// </summary>
        /// <remarks>
        /// Hand-rolled rather than regex-driven: fenced blocks are line-oriented, and a regex over
        /// the whole document mis-handles the case of a mermaid fence that is itself nested inside
        /// a wider fence — which is exactly how someone documents this feature.
        /// </remarks>
        public static IReadOnlyList<string> ExtractDiagrams(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return Array.Empty<string>();
            }

            var diagrams = new List<string>();
            var current = new StringBuilder();
            var inDiagram = false;

            foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                var trimmed = line.TrimStart();

                if (!inDiagram)
                {
                    if (IsFenceOpening(trimmed))
                    {
                        inDiagram = true;
                        current.Clear();
                    }

                    continue;
                }

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    if (current.Length > 0)
                    {
                        diagrams.Add(current.ToString().TrimEnd());
                    }

                    inDiagram = false;
                    continue;
                }

                current.Append(line).Append('\n');
            }

            // An unterminated fence at end of file still holds a usable diagram.
            if (inDiagram && current.Length > 0)
            {
                diagrams.Add(current.ToString().TrimEnd());
            }

            return diagrams;
        }

        private static bool IsFenceOpening(string trimmedLine)
        {
            if (!trimmedLine.StartsWith("```", StringComparison.Ordinal))
            {
                return false;
            }

            var info = trimmedLine.Substring(3).Trim();
            return info.Equals("mermaid", StringComparison.OrdinalIgnoreCase);
        }
    }
}
