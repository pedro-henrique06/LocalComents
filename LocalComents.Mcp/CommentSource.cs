using LocalComents.Services;

namespace LocalComents.Mcp
{
    /// <summary>
    /// Locates the JSON file written by the Visual Studio extension and points the shared
    /// <see cref="CommentStore"/> at it. Resolution order:
    /// <c>--file</c> argument, <c>LOCALCOMENTS_FILE</c> environment variable, a walk up from
    /// the working directory, then the user profile.
    /// </summary>
    internal static class CommentSource
    {
        public const string DefaultFileName = ".local-comments.json";

        /// <summary>Directory the comments file lives in — the root for generated documentation.</summary>
        public static string RootDirectory { get; private set; } = Directory.GetCurrentDirectory();

        public static void Initialize(string[] args)
        {
            var path = ResolvePath(args);

            RootDirectory = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            CommentStore.Instance.UseStorageFile(path);
        }

        private static string ResolvePath(string[] args)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "--file", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.GetFullPath(args[i + 1]);
                }
            }

            var fromEnvironment = Environment.GetEnvironmentVariable("LOCALCOMENTS_FILE");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return Path.GetFullPath(fromEnvironment);
            }

            var found = SearchUpwards(Directory.GetCurrentDirectory());
            if (found != null)
            {
                return found;
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                DefaultFileName);
        }

        private static string? SearchUpwards(string startDirectory)
        {
            var directory = new DirectoryInfo(startDirectory);

            while (directory != null)
            {
                var candidate = Path.Combine(directory.FullName, DefaultFileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }

            return null;
        }

        /// <summary>Shortens a path for display, relative to the storage folder when possible.</summary>
        public static string ToDisplayPath(string filePath)
        {
            if (filePath.StartsWith(RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(RootDirectory.Length).TrimStart(Path.DirectorySeparatorChar);
            }

            return filePath;
        }
    }
}
