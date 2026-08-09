using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace LocalComents.ToolWindows
{
    /// <summary>The "Local Comments Diagram" pane, showing the diagrams the agent generated.</summary>
    [Guid(PackageGuids.DiagramToolWindowString)]
    public sealed class DiagramToolWindow : ToolWindowPane
    {
        public DiagramToolWindow()
            : base(null)
        {
            Caption = "Local Comments Diagram";
            Content = new DiagramToolWindowControl();
        }
    }
}
