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
        public static void Write(string message)
        {
            Debug.WriteLine($"[LocalComents] {DateTime.Now:HH:mm:ss} {message}");
        }
    }
}
