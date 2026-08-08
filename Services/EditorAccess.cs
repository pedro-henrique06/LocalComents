using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;

namespace LocalComents.Services
{
    /// <summary>Bridges the shell (COM) editor services and the modern editor API.</summary>
    internal static class EditorAccess
    {
        public static IWpfTextView? GetActiveTextView(IServiceProvider serviceProvider)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (serviceProvider.GetService(typeof(SVsTextManager)) is not IVsTextManager textManager)
            {
                return null;
            }

            if (ErrorHandler.Failed(textManager.GetActiveView(1, null, out IVsTextView vsTextView)) || vsTextView == null)
            {
                return null;
            }

            return ToWpfTextView(serviceProvider, vsTextView);
        }

        public static void NavigateTo(IServiceProvider serviceProvider, string filePath, int zeroBasedLine, int zeroBasedColumn)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                VsShellUtilities.OpenDocument(
                    serviceProvider,
                    filePath,
                    VSConstants.LOGVIEWID.TextView_guid,
                    out _,
                    out _,
                    out IVsWindowFrame frame);

                frame?.Show();

                var vsTextView = VsShellUtilities.GetTextView(frame);
                if (vsTextView == null)
                {
                    return;
                }

                vsTextView.SetCaretPos(zeroBasedLine, Math.Max(zeroBasedColumn, 0));
                vsTextView.CenterLines(zeroBasedLine, 1);
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Failed to navigate to '{filePath}': {ex.Message}");
            }
        }

        private static IWpfTextView? ToWpfTextView(IServiceProvider serviceProvider, IVsTextView vsTextView)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var componentModel = serviceProvider.GetService(typeof(SComponentModel)) as IComponentModel;
            var adapterFactory = componentModel?.GetService<IVsEditorAdaptersFactoryService>();
            return adapterFactory?.GetWpfTextView(vsTextView);
        }
    }
}
