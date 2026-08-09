using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace LocalComents.Services
{
    /// <summary>
    /// Builds the HTML page that renders the diagrams, in a folder the WebView can be pointed at.
    /// <para>
    /// Page and script live in the same folder on purpose: served from one virtual host they are
    /// same-origin, which avoids the script-loading restrictions that apply to a document created
    /// from a string.
    /// </para>
    /// </summary>
    internal static class MermaidPage
    {
        public const string PageFileName = "diagram.html";
        private const string ScriptFileName = "mermaid.min.js";

        /// <summary>
        /// Ensures the render folder exists with the bundled script in it, and returns its path.
        /// Returns <c>null</c> when the script is missing from the installed extension.
        /// </summary>
        public static string? PrepareFolder()
        {
            var source = BundledScriptPath();
            if (source == null)
            {
                LocalComentsLog.Write($"'{ScriptFileName}' was not found next to the extension; the diagram view cannot render.");
                return null;
            }

            var folder = Path.Combine(Path.GetTempPath(), "LocalComents", "diagram");
            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, ScriptFileName);

            // Copied once per machine, then only when the shipped script changes — comparing length
            // is enough to catch an extension upgrade without hashing 3.5 MB on every open.
            if (!File.Exists(target) || new FileInfo(target).Length != new FileInfo(source).Length)
            {
                File.Copy(source, target, overwrite: true);
            }

            return folder;
        }

        /// <summary>Writes the page and returns its full path.</summary>
        public static string WritePage(string folder, IReadOnlyList<string> diagrams, bool darkTheme, string? emptyMessage)
        {
            var path = Path.Combine(folder, PageFileName);
            File.WriteAllText(path, BuildHtml(diagrams, darkTheme, emptyMessage), new UTF8Encoding(false));
            return path;
        }

        private static string BuildHtml(IReadOnlyList<string> diagrams, bool darkTheme, string? emptyMessage)
        {
            var background = darkTheme ? "#1f1f1f" : "#ffffff";
            var foreground = darkTheme ? "#d4d4d4" : "#1e1e1e";
            var muted = darkTheme ? "#9a9a9a" : "#6a6a6a";
            var theme = darkTheme ? "dark" : "default";

            var body = new StringBuilder();

            if (diagrams.Count == 0)
            {
                body.Append("<p class=\"empty\">").Append(Escape(emptyMessage ?? "No diagram to show.")).Append("</p>");
            }
            else
            {
                for (var i = 0; i < diagrams.Count; i++)
                {
                    body.Append("<figure><pre class=\"mermaid\">")
                        .Append(Escape(diagrams[i]))
                        .Append("</pre></figure>");
                }
            }

            return $@"<!doctype html>
<html>
<head>
<meta charset=""utf-8"">
<!-- No CSP meta on purpose: the same page is opened directly from disk as the fallback when the
     WebView is unavailable, and under a file:// origin 'self' blocks the sibling script. Label
     sanitising is handled by mermaid's securityLevel below. -->
<style>
  html, body {{ margin: 0; padding: 0; background: {background}; color: {foreground}; }}
  body {{ font-family: 'Segoe UI', sans-serif; font-size: 13px; padding: 12px; }}
  figure {{ margin: 0 0 24px 0; overflow-x: auto; }}
  pre.mermaid {{ margin: 0; }}
  .empty {{ color: {muted}; line-height: 1.5; }}
  .error {{ color: #e06c75; white-space: pre-wrap; font-family: Consolas, monospace; }}
</style>
<script src=""{ScriptFileName}""></script>
</head>
<body>
{body}
<script>
  (function () {{
    if (typeof mermaid === 'undefined') {{
      document.body.insertAdjacentHTML('beforeend',
        '<p class=""error"">mermaid.min.js failed to load.</p>');
      return;
    }}
    mermaid.initialize({{ startOnLoad: false, theme: '{theme}', securityLevel: 'strict' }});
    // suppressErrors keeps one malformed diagram from blanking the whole page: mermaid renders
    // its own error box in place and the rest still draw.
    mermaid.run({{ querySelector: '.mermaid', suppressErrors: true }});
  }})();
</script>
</body>
</html>";
        }

        /// <summary>The script as laid out by the VSIX, next to the extension assembly.</summary>
        private static string? BundledScriptPath()
        {
            var extensionDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (string.IsNullOrEmpty(extensionDirectory))
            {
                return null;
            }

            var candidate = Path.Combine(extensionDirectory!, "WebView", ScriptFileName);
            return File.Exists(candidate) ? candidate : null;
        }

        private static string Escape(string value)
            => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
