using System;

namespace LocalComents
{
    internal static class PackageGuids
    {
        public const string PackageString = "fa17b004-5ded-4df0-91c5-278e3d4f3f9d";
        public const string CmdSetString = "3f6b0c1e-9a4d-4e2b-8f31-6c5a7d2e1b04";
        public const string ToolWindowString = "7d2a5e93-1c48-4b6f-9a07-2e5f8c3b41d6";
        public const string DiagramToolWindowString = "5c8e1f27-64b3-4a90-bd15-8f3c2a7e6049";
        public const string OptionsPageString = "b1c9f8d4-3a27-4f56-8e10-9d4c7b6a2f38";

        public static readonly Guid CmdSet = new Guid(CmdSetString);
    }

    internal static class PackageIds
    {
        public const int CmdIdAddComment = 0x0100;
        public const int CmdIdOpenToolWindow = 0x0101;
        public const int CmdIdOpenDiagramWindow = 0x0102;
    }
}
