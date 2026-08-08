using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace LocalComents.ToolWindows
{
    /// <summary>The "Local Comments" pane, the Visual Studio counterpart of the VS Code sidebar.</summary>
    [Guid(PackageGuids.ToolWindowString)]
    public sealed class CommentsToolWindow : ToolWindowPane
    {
        public CommentsToolWindow()
            : base(null)
        {
            Caption = "Local Comments";
            Content = new CommentsToolWindowControl();
        }
    }
}
