using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalComents.Services
{
    /// <summary>
    /// Registers the bundled MCP server in <c>&lt;WorkspaceRoot&gt;\.vs\mcp.json</c>, one of the
    /// locations Visual Studio scans for MCP configuration. The root is the solution folder, or
    /// the opened folder in Open Folder mode.
    /// <para>
    /// Writing the file from the package — instead of shipping a static one inside the VSIX — is
    /// what makes the server actually usable: only at runtime do we know the absolute path of the
    /// executable (a bare file name would not resolve, since the VSIX folder is not on PATH) and
    /// the storage file the user's options currently point at (the server would otherwise walk up
    /// from an unrelated working directory and silently report zero comments).
    /// </para>
    /// </summary>
    internal static class McpServerRegistration
    {
        /// <summary>Key used for the server in <c>mcp.json</c>; also what the chat tool picker shows.</summary>
        public const string ServerName = "local-comments";

        private const string ServersProperty = "servers";

        /// <summary>Path of the server relative to the extension's install folder, as laid out by the VSIX.</summary>
        private static readonly string RelativeServerPath = Path.Combine("MCP", "LocalComents.Mcp.exe");

        /// <summary>
        /// Every config file this session has written an entry into. Tracked so all of them can be
        /// cleaned up on shutdown, not just whichever solution happened to be open last.
        /// </summary>
        private static readonly HashSet<string> RegisteredConfigPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Brings <c>.vs\mcp.json</c> in line with the current configuration. Adds or updates our
        /// entry when <paramref name="enabled"/>, removes it otherwise, and leaves any other
        /// server in the file untouched. Does nothing when no workspace is open.
        /// </summary>
        public static void Update(string? workspaceDirectory, string? storageFile, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(workspaceDirectory))
            {
                return;
            }

            // Dropped rather than merely skipped when the server executable cannot be found: an
            // entry written by a previous install must not outlive the file it points at.
            var entry = enabled ? BuildEntry(storageFile) : null;

            Apply(Path.Combine(solutionDirectory!, ".vs", "mcp.json"), entry);
        }

        /// <summary>
        /// Takes our entry back out of every file it was written to.
        /// <para>
        /// Called as the package is disposed, because nothing of ours runs once the extension is
        /// uninstalled — a registration left behind would point Visual Studio at an executable that
        /// no longer exists. The entry is rewritten unchanged on the next solution load, so the
        /// trust baseline Visual Studio keeps for the server is not disturbed.
        /// </para>
        /// This only touches the file, never a shell service, so it is safe during shutdown.
        /// </summary>
        public static void RemoveAll()
        {
            string[] paths;
            lock (RegisteredConfigPaths)
            {
                paths = RegisteredConfigPaths.ToArray();
                RegisteredConfigPaths.Clear();
            }

            foreach (var path in paths)
            {
                Apply(path, null);
            }
        }

        /// <summary>Merges <paramref name="entry"/> into the file, or removes ours when it is <c>null</c>.</summary>
        private static void Apply(string configPath, JObject? entry)
        {
            try
            {
                var document = Read(configPath);
                if (document == null)
                {
                    // Unreadable or hand-edited into something we do not understand: leave it alone
                    // rather than destroy configuration the user may depend on.
                    return;
                }

                var before = document.DeepClone();

                if (entry != null)
                {
                    var servers = document[ServersProperty] as JObject;
                    if (servers == null)
                    {
                        servers = new JObject();
                        document[ServersProperty] = servers;
                    }

                    servers[ServerName] = entry;
                }
                else
                {
                    (document[ServersProperty] as JObject)?.Remove(ServerName);
                }

                Track(configPath, entry != null);

                if (JToken.DeepEquals(before, document))
                {
                    // Saving an unchanged file would still restart the Copilot agent, so skip it.
                    return;
                }

                Write(configPath, document);
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Failed to update '{configPath}': {ex.Message}");
            }
        }

        private static void Track(string configPath, bool registered)
        {
            lock (RegisteredConfigPaths)
            {
                if (registered)
                {
                    RegisteredConfigPaths.Add(configPath);
                }
                else
                {
                    RegisteredConfigPaths.Remove(configPath);
                }
            }
        }

        /// <summary>Reads the existing config, or an empty document when there is none. <c>null</c> means "do not touch".</summary>
        private static JObject? Read(string configPath)
        {
            if (!File.Exists(configPath))
            {
                return new JObject();
            }

            var content = File.ReadAllText(configPath);
            if (string.IsNullOrWhiteSpace(content))
            {
                return new JObject();
            }

            try
            {
                return JObject.Parse(content);
            }
            catch (JsonException ex)
            {
                LocalComentsLog.Write($"'{configPath}' is not valid JSON, leaving it untouched: {ex.Message}");
                return null;
            }
        }

        private static void Write(string configPath, JObject document)
        {
            var servers = document[ServersProperty] as JObject;

            // An empty file is not valid MCP configuration, and leaving one behind would make the
            // "off" state indistinguishable from a broken config.
            if (document.Count == 0 || (document.Count == 1 && servers != null && servers.Count == 0))
            {
                if (File.Exists(configPath))
                {
                    File.Delete(configPath);
                }

                return;
            }

            var directory = Path.GetDirectoryName(configPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory!);
            }

            File.WriteAllText(configPath, document.ToString(Formatting.Indented));
        }

        private static JObject? BuildEntry(string? storageFile)
        {
            var executable = ResolveServerExecutable();
            if (executable == null)
            {
                LocalComentsLog.Write(
                    "The bundled MCP server was not found next to the extension; removing any existing registration.");
                return null;
            }

            var entry = new JObject
            {
                ["type"] = "stdio",
                ["command"] = executable,
            };

            // Without --file the server walks up from its working directory, which Visual Studio
            // does not guarantee to be the solution folder.
            entry["args"] = string.IsNullOrWhiteSpace(storageFile)
                ? new JArray()
                : new JArray("--file", storageFile!);

            return entry;
        }

        private static string? ResolveServerExecutable()
        {
            var extensionDirectory = Path.GetDirectoryName(typeof(McpServerRegistration).Assembly.Location);
            if (string.IsNullOrEmpty(extensionDirectory))
            {
                return null;
            }

            var candidate = Path.Combine(extensionDirectory!, RelativeServerPath);
            return File.Exists(candidate) ? candidate : null;
        }
    }
}
