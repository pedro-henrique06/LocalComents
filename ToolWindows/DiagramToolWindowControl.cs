using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using LocalComents.Services;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Task = System.Threading.Tasks.Task;

namespace LocalComents.ToolWindows
{
    /// <summary>
    /// Renders the Mermaid diagrams from the document the agent generated through the MCP server.
    /// <para>
    /// It shows what is on disk rather than analysing the project: the diagram is refreshed by
    /// rerunning the <c>generate_documentation</c> prompt, and the file watcher picks that up, so
    /// the view follows the document without the extension understanding any of the code.
    /// </para>
    /// </summary>
    public sealed class DiagramToolWindowControl : UserControl
    {
        private const string VirtualHost = "localcomments.diagram";

        private readonly Border _host;
        private readonly TextBlock _status;
        private readonly DispatcherTimer _debounce;

        private WebView2? _webView;
        private bool _webViewUnavailable;
        private string? _webViewFailure;
        private FileSystemWatcher? _watcher;
        private string? _pagePath;

        public DiagramToolWindowControl()
        {
            this.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            this.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            FontSize = 12;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 8, 8, 6),
            };

            var refresh = ThemedButton("Refresh", "Re-read the generated document from disk");
            refresh.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Render();
            };
            header.Children.Add(refresh);

            var open = ThemedButton("Open in browser", "Open the rendered diagram in the default browser");
            open.Margin = new Thickness(6, 0, 0, 0);
            open.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                OpenInBrowser();
            };
            header.Children.Add(open);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            _host = new Border { Margin = new Thickness(8, 0, 8, 0) };
            Grid.SetRow(_host, 1);
            root.Children.Add(_host);

            _status = new TextBlock
            {
                Margin = new Thickness(8, 6, 8, 6),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            Grid.SetRow(_status, 2);
            root.Children.Add(_status);

            Content = root;

            // The agent rewrites the document in one go, but editors and tools can produce several
            // change notifications per save; coalesce them into one render.
            _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                Render();
            };

            Loaded += (_, _) => Render();
            Unloaded += (_, _) => StopWatching();
        }

        private void Render()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var folder = MermaidDocument.ResolveFolder();
            if (folder == null)
            {
                ShowMessage("Open a solution or folder first — documents are looked up next to the comments file.");
                return;
            }

            StartWatching(folder);

            var documentPath = MermaidDocument.FindDocument(folder);
            if (documentPath == null)
            {
                // The folder is part of the message on purpose: with the storage location set to
                // User or Custom the document lands somewhere the reader would not think to look,
                // and an empty window with no path is indistinguishable from a broken one.
                ShowMessage(
                    "No Markdown with a mermaid diagram here yet.\n\n" +
                    "In Copilot Chat (Agent mode), run the MCP prompt \"generate_documentation\" — " +
                    "it writes the document and the diagram appears here.\n\n" +
                    $"Looking in: {folder}");

                _status.Text = $"Waiting for a document in {folder}";
                return;
            }

            IReadOnlyList<string> diagrams;
            try
            {
                diagrams = MermaidDocument.ExtractDiagrams(File.ReadAllText(documentPath));
            }
            catch (IOException ex)
            {
                ShowMessage($"Could not read '{documentPath}': {ex.Message}");
                return;
            }

            var renderFolder = MermaidPage.PrepareFolder();
            if (renderFolder == null)
            {
                ShowMessage("The bundled mermaid script is missing from the installed extension.");
                return;
            }

            var empty = diagrams.Count == 0
                ? $"{Path.GetFileName(documentPath)} has no ```mermaid block."
                : null;

            _pagePath = MermaidPage.WritePage(renderFolder, diagrams, IsDarkTheme(), empty);

            _status.Text = diagrams.Count == 0
                ? $"No diagram in {documentPath}"
                : $"{diagrams.Count} diagram(s) from {documentPath}";

            ShowPage(renderFolder);
        }

        private void ShowPage(string folder)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_webViewUnavailable)
            {
                ShowWebViewFallback();
                return;
            }

            _ = ShowPageAsync(folder);
        }

        private async Task ShowPageAsync(string folder)
        {
            try
            {
                if (_webView == null)
                {
                    _webView = CreateWebView();
                    _host.Child = _webView;
                    await InitializeWebViewAsync(_webView, folder);
                }

                NavigateWebView(_webView);
            }
            catch (Exception ex)
            {
                // WebView2 is resolved from the IDE rather than shipped in the VSIX, so this is
                // where a runtime that is absent or a version that will not bind surfaces. The
                // rendered page is already on disk, so degrade to opening it externally instead
                // of leaving the window blank.
                LocalComentsLog.Write($"The WebView could not be initialised: {ex}");
                _webViewUnavailable = true;
                _webView = null;
                _webViewFailure = $"{ex.GetType().Name}: {ex.Message}";

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                ShowWebViewFallback();
            }
        }

        // Kept out of the caller so the WebView2 assemblies are only resolved once execution is
        // already inside the try block above; a failure to load them would otherwise be thrown
        // when the calling method is compiled, escaping the handler.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WebView2 CreateWebView() => new WebView2();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task InitializeWebViewAsync(WebView2 webView, string folder)
        {
            await webView.EnsureCoreWebView2Async(null);

            // Page and script are served from one host so they are same-origin.
            webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);

            webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void NavigateWebView(WebView2 webView)
        {
            // The query string only exists to make each render a distinct URL: navigating to the
            // address already loaded is a no-op, and a Navigate/Reload pair races instead.
            var url = $"https://{VirtualHost}/{MermaidPage.PageFileName}?r={DateTime.UtcNow.Ticks}";
            webView.CoreWebView2.Navigate(url);
        }

        private void ShowWebViewFallback()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // The reason is on screen rather than only in the trace log: the shipped VSIX is a
            // Release build with no debugger attached, so a message written to Trace is a message
            // nobody will ever read, and "not available" alone is not something anyone can act on.
            var reason = _webViewFailure == null
                ? string.Empty
                : $"\n\nReason: {_webViewFailure}";

            ShowMessage(
                "The embedded browser is not available in this Visual Studio instance.\n\n" +
                "The diagram was still rendered — use \"Open in browser\" above to view it." +
                reason);
        }

        private void OpenInBrowser()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_pagePath == null || !File.Exists(_pagePath))
            {
                Render();
            }

            if (_pagePath == null || !File.Exists(_pagePath))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(_pagePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Could not open '{_pagePath}': {ex.Message}");
            }
        }

        private void ShowMessage(string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var text = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 8, 4, 4),
                LineHeight = 18,
            };
            text.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            _host.Child = new ScrollViewer
            {
                Content = text,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
        }

        /// <summary>
        /// Watches every Markdown file in the folder, not one name: the agent picks the file name,
        /// so a watcher bound to one would miss the document that actually gets written.
        /// </summary>
        private void StartWatching(string folder)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }

            if (_watcher != null && string.Equals(_watcher.Path, folder, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            StopWatching();

            try
            {
                _watcher = new FileSystemWatcher(folder, "*.md")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };

                _watcher.Changed += OnDocumentChanged;
                _watcher.Created += OnDocumentChanged;
                _watcher.Renamed += OnDocumentChanged;
                _watcher.Deleted += OnDocumentChanged;
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Could not watch '{folder}': {ex.Message}");
            }
        }

        private void OnDocumentChanged(object sender, FileSystemEventArgs e)
        {
            // Raised on a watcher thread; the debounce timer belongs to the UI dispatcher. Nothing
            // waits on the redraw, and a failure must not propagate back into the watcher.
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                _debounce.Stop();
                _debounce.Start();
            }).FileAndForget("LocalComents/Diagram/Reload");
#pragma warning restore VSSDK007
        }

        private void StopWatching()
        {
            if (_watcher == null)
            {
                return;
            }

            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnDocumentChanged;
            _watcher.Created -= OnDocumentChanged;
            _watcher.Renamed -= OnDocumentChanged;
            _watcher.Deleted -= OnDocumentChanged;
            _watcher.Dispose();
            _watcher = null;
        }

        private static bool IsDarkTheme()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var background = VSColorTheme.GetThemedColor(EnvironmentColors.ToolWindowBackgroundColorKey);

            // Perceived luminance; the shell exposes the colour, not the theme's own light/dark flag.
            var luminance = ((0.299 * background.R) + (0.587 * background.G) + (0.114 * background.B)) / 255.0;
            return luminance < 0.5;
        }

        private static Button ThemedButton(string content, string tooltip)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 58,
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = tooltip,
            };
            button.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            return button;
        }
    }
}
