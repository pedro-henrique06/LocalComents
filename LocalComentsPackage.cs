using System;
using System.ComponentModel.Design;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LocalComents.Commands;
using LocalComents.Options;
using LocalComents.Services;
using LocalComents.ToolWindows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace LocalComents
{
    /// <summary>
    /// Entry point of the extension: wires the commands, the tool window, the options page
    /// and points <see cref="CommentStore"/> at the right storage file for the current solution.
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuids.PackageString)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideOptionPage(typeof(LocalComentsOptionsPage), "Local Comments", "General", 0, 0, true)]
    [ProvideToolWindow(typeof(CommentsToolWindow), Style = VsDockStyle.Tabbed, Window = SolutionExplorerGuid)]
    [ProvideToolWindow(typeof(DiagramToolWindow), Style = VsDockStyle.Tabbed, Window = SolutionExplorerGuid)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.NoSolution_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class LocalComentsPackage : AsyncPackage
    {
        private const string SolutionExplorerGuid = "3ae79031-e1bc-11d0-8f78-00a0c9110057";

        public const string PackageGuidString = PackageGuids.PackageString;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            await AddCommentCommand.InitializeAsync(this);
            RegisterOpenToolWindowCommand();

            LocalComentsOptionsPage.Changed += (_, _) => RefreshConfiguration();
            Microsoft.VisualStudio.Shell.Events.SolutionEvents.OnAfterOpenSolution += (_, _) => RefreshConfiguration();
            Microsoft.VisualStudio.Shell.Events.SolutionEvents.OnAfterCloseSolution += (_, _) => RefreshConfiguration();

            // Open Folder mode raises its own pair of events; without these the workspace root is
            // never picked up and everything silently falls back to the user profile.
            Microsoft.VisualStudio.Shell.Events.SolutionEvents.OnAfterOpenFolder += (_, _) => RefreshConfiguration();
            Microsoft.VisualStudio.Shell.Events.SolutionEvents.OnAfterCloseFolder += (_, _) => RefreshConfiguration();

            RefreshConfiguration();
        }

        private void RegisterOpenToolWindowCommand()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetService(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
            {
                return;
            }

            commandService.AddCommand(new MenuCommand(
                (_, _) => ShowToolWindow(typeof(CommentsToolWindow)),
                new CommandID(PackageGuids.CmdSet, PackageIds.CmdIdOpenToolWindow)));

            commandService.AddCommand(new MenuCommand(
                (_, _) => ShowToolWindow(typeof(DiagramToolWindow)),
                new CommandID(PackageGuids.CmdSet, PackageIds.CmdIdOpenDiagramWindow)));
        }

        private void ShowToolWindow(Type toolWindowType)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = FindToolWindow(toolWindowType, 0, true);
            if (window?.Frame is IVsWindowFrame frame)
            {
                ErrorHandler.ThrowOnFailure(frame.Show());
            }
        }

        /// <summary>Re-reads the options and re-points the store, e.g. after a solution is opened.</summary>
        private void RefreshConfiguration()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var options = (LocalComentsOptionsPage)GetDialogPage(typeof(LocalComentsOptionsPage));

            LocalComentsSettings.ShowGlyph = options.ShowGlyph;
            LocalComentsSettings.HighlightRange = options.HighlightRange;
            LocalComentsSettings.ShowInlineText = options.ShowInlineText;
            LocalComentsSettings.HideStaleComments = options.HideStaleComments;

            var fileName = string.IsNullOrWhiteSpace(options.FileName) ? ".local-comments.json" : options.FileName.Trim();
            var workspaceFolder = GetWorkspaceFolder();
            var folder = ResolveStorageFolder(options, workspaceFolder);

            string? storageFile = null;

            if (!string.IsNullOrEmpty(folder))
            {
                storageFile = Path.Combine(folder, fileName);
                CommentStore.Instance.UseStorageFile(storageFile);
            }

            // The MCP server is a separate process: it only finds the right file if we hand it the
            // resolved path, so the registration has to be refreshed alongside the storage itself.
            McpServerRegistration.Update(workspaceFolder, storageFile, options.RegisterMcpServer);
        }

        private string ResolveStorageFolder(LocalComentsOptionsPage options, string? workspaceFolder)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var userFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            switch (options.SaveLocation)
            {
                case SaveLocation.User:
                    return userFolder;

                case SaveLocation.Custom:
                    return string.IsNullOrWhiteSpace(options.CustomFolder) ? userFolder : options.CustomFolder.Trim();

                default:
                    return workspaceFolder ?? userFolder;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // The registration points at an executable inside this extension's folder, and
                // nothing of ours runs once the extension is uninstalled. Taking it out here is
                // the last chance to avoid leaving Visual Studio pointed at a path that is about
                // to disappear; the next solution load writes it back.
                McpServerRegistration.RemoveAll();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// The folder the comments and the MCP registration belong to: the solution's folder, or
        /// the opened folder in Open Folder mode.
        /// </summary>
        private string? GetWorkspaceFolder()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetService(typeof(SVsSolution)) is not IVsSolution solution)
            {
                return null;
            }

            if (ErrorHandler.Succeeded(solution.GetSolutionInfo(out var directory, out _, out _))
                && !string.IsNullOrWhiteSpace(directory))
            {
                return directory;
            }

            // Open Folder mode has no solution file, so GetSolutionInfo comes back empty. The
            // opened folder is still reachable as the solution directory property.
            if (!IsInOpenFolderMode(solution))
            {
                return null;
            }

            if (ErrorHandler.Failed(solution.GetProperty((int)__VSPROPID.VSPROPID_SolutionDirectory, out var value)))
            {
                return null;
            }

            return value as string is { Length: > 0 } folder ? folder : null;
        }

        private static bool IsInOpenFolderMode(IVsSolution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            return ErrorHandler.Succeeded(
                       solution.GetProperty((int)__VSPROPID7.VSPROPID_IsInOpenFolderMode, out var value))
                   && value is bool inFolderMode
                   && inFolderMode;
        }
    }
}
