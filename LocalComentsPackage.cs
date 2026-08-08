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

            RefreshConfiguration();
        }

        private void RegisterOpenToolWindowCommand()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetService(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
            {
                return;
            }

            var commandId = new CommandID(PackageGuids.CmdSet, PackageIds.CmdIdOpenToolWindow);
            commandService.AddCommand(new MenuCommand((_, _) => ShowToolWindow(), commandId));
        }

        private void ShowToolWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var window = FindToolWindow(typeof(CommentsToolWindow), 0, true);
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
            var folder = ResolveStorageFolder(options);

            if (!string.IsNullOrEmpty(folder))
            {
                CommentStore.Instance.UseStorageFile(Path.Combine(folder, fileName));
            }
        }

        private string ResolveStorageFolder(LocalComentsOptionsPage options)
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
                    return GetSolutionFolder() ?? userFolder;
            }
        }

        private string? GetSolutionFolder()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (GetService(typeof(SVsSolution)) is not IVsSolution solution)
            {
                return null;
            }

            if (ErrorHandler.Failed(solution.GetSolutionInfo(out var directory, out _, out _)))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(directory) ? null : directory;
        }
    }
}
