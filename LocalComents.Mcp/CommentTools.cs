using System.ComponentModel;
using LocalComents.Models;
using LocalComents.Services;
using ModelContextProtocol.Server;
using Newtonsoft.Json;

namespace LocalComents.Mcp
{
    /// <summary>
    /// Read access to the local comments, plus a single write tool for the generated
    /// documentation. Everything is returned as JSON so the agent gets structure rather
    /// than prose it has to re-parse.
    /// </summary>
    [McpServerToolType]
    internal static class CommentTools
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore,
        };

        [McpServerTool(Name = "list_files_with_comments")]
        [Description("Lists every file that has local comments, with how many each one has. Use this first to get an overview of where the annotations are concentrated.")]
        public static string ListFilesWithComments()
        {
            CommentStore.Instance.Reload();

            var files = CommentStore.Instance.GetAll()
                .Select(pair => new
                {
                    file = CommentSource.ToDisplayPath(pair.Key),
                    absolutePath = pair.Key,
                    count = pair.Value.Count,
                })
                .OrderByDescending(f => f.count)
                .ToArray();

            return JsonConvert.SerializeObject(
                new { storageFile = CommentStore.Instance.StoragePath, totalFiles = files.Length, files },
                Json);
        }

        [McpServerTool(Name = "get_comments")]
        [Description("Returns the local comments with their text, the line they are anchored to and the code snippet they refer to. Omit filePath to get every comment in the project.")]
        public static string GetComments(
            [Description("Optional file to filter by. Accepts an absolute path or a file name such as 'Program.cs'.")]
            string? filePath = null)
        {
            CommentStore.Instance.Reload();

            var files = CommentStore.Instance.GetAll();

            if (!string.IsNullOrWhiteSpace(filePath))
            {
                files = files
                    .Where(pair => Matches(pair.Key, filePath!))
                    .ToArray();
            }

            return JsonConvert.SerializeObject(Project(files), Json);
        }

        [McpServerTool(Name = "search_comments")]
        [Description("Full-text search across comment text, the anchored code snippet and file names. Use it to pull the annotations relevant to one topic instead of loading all of them.")]
        public static string SearchComments(
            [Description("Text to look for. Case-insensitive substring match.")]
            string query)
        {
            CommentStore.Instance.Reload();

            if (string.IsNullOrWhiteSpace(query))
            {
                return JsonConvert.SerializeObject(new { matches = Array.Empty<object>() }, Json);
            }

            var files = CommentStore.Instance.GetAll()
                .Select(pair => new KeyValuePair<string, IReadOnlyList<LocalComment>>(
                    pair.Key,
                    pair.Value.Where(c => MatchesQuery(pair.Key, c, query)).ToArray()))
                .Where(pair => pair.Value.Count > 0)
                .ToArray();

            return JsonConvert.SerializeObject(Project(files), Json);
        }

        [McpServerTool(Name = "write_documentation")]
        [Description("Writes generated Markdown to a file next to the comments storage file and returns the absolute path. Use this to deliver the final documentation, including any ```mermaid diagram blocks.")]
        public static string WriteDocumentation(
            [Description("The full Markdown document to write.")]
            string markdown,
            [Description("Optional file name. Defaults to DOCUMENTATION.md. Must be a bare file name, not a path.")]
            string? fileName = null)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                throw new ArgumentException("The markdown content is empty.", nameof(markdown));
            }

            var name = string.IsNullOrWhiteSpace(fileName) ? "DOCUMENTATION.md" : fileName!.Trim();

            // The agent controls this value, so keep the write inside the known root.
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
                || !string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{fileName}' is not a bare file name.", nameof(fileName));
            }

            if (!name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                name += ".md";
            }

            var target = Path.Combine(CommentSource.RootDirectory, name);
            File.WriteAllText(target, markdown);

            return JsonConvert.SerializeObject(
                new { written = true, path = target, bytes = markdown.Length },
                Json);
        }

        private static object Project(IReadOnlyList<KeyValuePair<string, IReadOnlyList<LocalComment>>> files)
        {
            var projected = files
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new
                {
                    file = CommentSource.ToDisplayPath(pair.Key),
                    absolutePath = pair.Key,
                    comments = pair.Value
                        .OrderBy(c => c.Range?.StartLine ?? 0)
                        .Select(c => new
                        {
                            text = c.Text,
                            // Stored zero-based to stay compatible with VS Code; surfaced one-based.
                            line = (c.Range?.StartLine ?? 0) + 1,
                            endLine = (c.Range?.EndLine ?? 0) + 1,
                            code = c.Range?.SelectedText?.Trim(),
                            createdAt = c.Timestamp > 0
                                ? c.CreatedAtLocal.ToString("yyyy-MM-dd HH:mm")
                                : null,
                        })
                        .ToArray(),
                })
                .ToArray();

            return new
            {
                totalFiles = projected.Length,
                totalComments = projected.Sum(f => f.comments.Length),
                files = projected,
            };
        }

        private static bool Matches(string storedPath, string filter)
        {
            return storedPath.Equals(filter, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(storedPath).Equals(filter, StringComparison.OrdinalIgnoreCase)
                || storedPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool MatchesQuery(string filePath, LocalComment comment, string query)
        {
            return Contains(comment.Text, query)
                || Contains(comment.Range?.SelectedText, query)
                || Contains(Path.GetFileName(filePath), query);
        }

        private static bool Contains(string? value, string query)
            => !string.IsNullOrEmpty(value) && value!.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
