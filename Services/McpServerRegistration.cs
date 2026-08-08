using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LocalComents.Services
{
    /// <summary>
    /// Registers the bundled MCP server in <c>&lt;SolutionDir&gt;\.vs\mcp.json</c>, one of the
    /// locations Visual Studio scans for MCP configuration.
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
        /// Brings <c>.vs\mcp.json</c> in line with the current configuration. Adds or updates our
        /// entry when <paramref name="enabled"/>, removes it otherwise, and leaves any other
        /// server in the file untouched. Does nothing when no solution is open.
        /// </summary>
        public static void Update(string? solutionDirectory, string? storageFile, bool enabled)
        {
            if (string.IsNullOrWhiteSpace(solutionDirectory))
            {
                return;
            }

            var configPath = Path.Combine(solutionDirectory!, ".vs", "mcp.json");

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

                if (enabled)
                {
                    var entry = BuildEntry(storageFile);
                    if (entry == null)
                    {
                        return;
                    }

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
                LocalComentsLog.Write("The bundled MCP server was not found next to the extension; skipping registration.");
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
