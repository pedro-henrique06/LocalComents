using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>
        /// The document to render: <c>DOCUMENTATION.md</c> beside the comments storage file.
        /// Returns <c>null</c> when no storage file is configured.
        /// </summary>
        public static string? ResolvePath()
        {
            var storagePath = CommentStore.Instance.StoragePath;
            if (string.IsNullOrWhiteSpace(storagePath))
            {
                return null;
            }

            var folder = Path.GetDirectoryName(storagePath);
            return string.IsNullOrEmpty(folder)
                ? null
                : Path.Combine(folder!, DefaultFileName);
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
