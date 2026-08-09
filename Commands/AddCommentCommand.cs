using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using LocalComents.Editor;
using LocalComents.Models;
using LocalComents.Services;
using LocalComents.UI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Task = System.Threading.Tasks.Task;

namespace LocalComents.Commands
{
    /// <summary>
    /// Adds a comment to the current selection, or to the whole caret line when nothing is
    /// selected. Bound to Alt+C in the .vsct file.
    /// </summary>
    internal static class AddCommentCommand
    {
        public static async Task InitializeAsync(AsyncPackage package)
        {
            await package.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (await package.GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commandService)
            {
                return;
            }

            var commandId = new CommandID(PackageGuids.CmdSet, PackageIds.CmdIdAddComment);
            commandService.AddCommand(new MenuCommand((s, e) => Execute(package), commandId));
        }

        private static void Execute(AsyncPackage package)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var view = EditorAccess.GetActiveTextView(package);
            if (view == null)
            {
                ShowInfo(package, "Open a document before adding a local comment.");
                return;
            }

            var filePath = view.TextBuffer.GetFilePath();
            if (string.IsNullOrEmpty(filePath))
            {
                ShowInfo(package, "Save the document first: local comments are keyed by file path.");
                return;
            }

            var range = BuildRange(view, out var anchorText);
            var input = CommentInputDialog.Prompt("Add local comment", anchorText);
            if (input == null)
            {
                return;
            }

            var comment = new LocalComment
            {
                Text = input.Text,
                Color = input.ColorId,
                Timestamp = LocalComment.NowTimestamp(),
                Range = range,
            };

            CommentStore.Instance.Add(filePath!, comment);
        }

        private static CommentRange BuildRange(IWpfTextView view, out string anchorText)
        {
            var snapshot = view.TextSnapshot;
            var selection = view.Selection;

            if (!selection.IsEmpty)
            {
                var start = selection.Start.Position;
                var end = selection.End.Position;
                var startLine = snapshot.GetLineFromPosition(start);
                var endLine = snapshot.GetLineFromPosition(end);

                anchorText = new SnapshotSpan(start, end).GetText();

                return new CommentRange
                {
                    StartLine = startLine.LineNumber,
                    StartCharacter = start.Position - startLine.Start.Position,
                    EndLine = endLine.LineNumber,
                    EndCharacter = end.Position - endLine.Start.Position,
                    SelectedText = anchorText,
                };
            }

            var caretLine = snapshot.GetLineFromPosition(view.Caret.Position.BufferPosition);
            anchorText = caretLine.GetText();

            return new CommentRange
            {
                StartLine = caretLine.LineNumber,
                StartCharacter = 0,
                EndLine = caretLine.LineNumber,
                EndCharacter = CommentRange.WholeLineEndCharacter,
                SelectedText = anchorText,
            };
        }

        private static void ShowInfo(IServiceProvider serviceProvider, string message)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            VsShellUtilities.ShowMessageBox(
                serviceProvider,
                message,
                "Local Comments",
                Microsoft.VisualStudio.Shell.Interop.OLEMSGICON.OLEMSGICON_INFO,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGBUTTON.OLEMSGBUTTON_OK,
                Microsoft.VisualStudio.Shell.Interop.OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
        }
    }
}
