using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace LocalComents.Options
{
    public enum SaveLocation
    {
        /// <summary>Next to the open solution (falls back to the user profile when none is open).</summary>
        Solution = 0,

        /// <summary>In the current user's profile folder.</summary>
        User = 1,

        /// <summary>In the folder given by <see cref="LocalComentsOptionsPage.CustomFolder"/>.</summary>
        Custom = 2,
    }

    [Guid(PackageGuids.OptionsPageString)]
    public sealed class LocalComentsOptionsPage : DialogPage
    {
        [Category("Storage")]
        [DisplayName("Save location")]
        [Description("Where the comments file lives. 'Solution' keeps annotations next to the code, 'User' keeps a single global file.")]
        public SaveLocation SaveLocation { get; set; } = SaveLocation.Solution;

        [Category("Storage")]
        [DisplayName("Custom folder")]
        [Description("Folder used when 'Save location' is set to Custom.")]
        public string CustomFolder { get; set; } = string.Empty;

        [Category("Storage")]
        [DisplayName("File name")]
        [Description("Name of the JSON file holding the comments.")]
        public string FileName { get; set; } = ".local-comments.json";

        [Category("Behavior")]
        [DisplayName("Show glyph in the margin")]
        [Description("Draws a marker in the indicator margin for every line that has a comment.")]
        public bool ShowGlyph { get; set; } = true;

        [Category("Behavior")]
        [DisplayName("Highlight commented code")]
        [Description("Highlights the commented range inside the editor.")]
        public bool HighlightRange { get; set; } = true;

        [Category("Behavior")]
        [DisplayName("Show comment text inline")]
        [Description("Draws the comment text at the end of the annotated line, like an inline hint.")]
        public bool ShowInlineText { get; set; } = true;

        [Category("Behavior")]
        [DisplayName("Hide stale comments")]
        [Description("Hides comments whose anchor text no longer matches the code at that position.")]
        public bool HideStaleComments { get; set; }

        public override void SaveSettingsToStorage()
        {
            base.SaveSettingsToStorage();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Raised after the user applies changes in Tools > Options.</summary>
        public static event EventHandler? Changed;
    }
}
