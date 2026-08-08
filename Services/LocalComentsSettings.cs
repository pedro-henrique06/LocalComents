namespace LocalComents.Services
{
    /// <summary>
    /// Plain snapshot of the user options. The MEF editor components cannot reach the
    /// package's <c>DialogPage</c> from a background thread, so the package pushes the
    /// values here whenever they change.
    /// </summary>
    public static class LocalComentsSettings
    {
        public static bool ShowGlyph { get; set; } = true;

        public static bool HighlightRange { get; set; } = true;

        public static bool ShowInlineText { get; set; } = true;

        public static bool HideStaleComments { get; set; }
    }
}
