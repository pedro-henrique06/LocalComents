using System;
using System.Diagnostics;

namespace LocalComents.Services
{
    /// <summary>
    /// Minimal diagnostics sink. Editor components run on background threads where the
    /// VS activity log is awkward to reach, so failures are traced instead of thrown.
    /// </summary>
    internal static class LocalComentsLog
    {
        /// <remarks>
        /// <see cref="Trace"/> rather than <see cref="Debug"/>: the shipped VSIX and the MCP server
        /// are Release builds, where <c>Debug.WriteLine</c> compiles away and every diagnostic here
        /// would vanish — exactly when it is most needed.
        /// </remarks>
        public static void Write(string message)
        {
            Trace.WriteLine($"[LocalComents] {DateTime.Now:HH:mm:ss} {message}");
        }
    }
}
