using System;
using System.Windows.Media;
using LocalComents.Services;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Classification;

namespace LocalComents.Editor
{
    /// <summary>
    /// Reads and writes how strongly the code highlight is painted.
    /// <para>
    /// The value lives in the editor's format map rather than in this extension's options: that is
    /// already where the marker colours come from, Visual Studio persists changes to it per theme,
    /// and it means the slider and <em>Fonts and Colors</em> are editing the same thing instead of
    /// two settings that can disagree.
    /// </para>
    /// <para>
    /// One value for every comment, not one per comment. The highlight is drawn through a named
    /// <see cref="MarkerFormatDefinition"/>, so a per-comment opacity would need a separate
    /// definition for each colour and level — and unlike colour, opacity carries no meaning worth
    /// varying between annotations.
    /// </para>
    /// </summary>
    internal static class HighlightOpacity
    {
        public const int MinimumPercent = 5;
        public const int MaximumPercent = 100;

        /// <summary>Matches the alpha the palette ships with.</summary>
        public const int DefaultPercent = 30;

        /// <summary>Category the text views draw from.</summary>
        private const string TextCategory = "text";

        /// <summary>Current opacity, read from the default entry — they are kept in step.</summary>
        public static int GetPercent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var map = TryGetFormatMap();
            if (map == null)
            {
                return DefaultPercent;
            }

            try
            {
                var properties = map.GetProperties(CommentPalette.Yellow.MarkerFormatName);
                if (properties != null
                    && properties.Contains(EditorFormatDefinition.BackgroundColorId)
                    && properties[EditorFormatDefinition.BackgroundColorId] is Color colour)
                {
                    return ToPercent(colour.A);
                }
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Could not read the highlight opacity: {ex.Message}");
            }

            return DefaultPercent;
        }

        /// <summary>Applies <paramref name="percent"/> to every palette entry.</summary>
        public static void SetPercent(int percent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var map = TryGetFormatMap();
            if (map == null)
            {
                return;
            }

            var alpha = ToAlpha(percent);

            try
            {
                map.BeginBatchUpdate();

                foreach (var entry in CommentPalette.Entries)
                {
                    var properties = map.GetProperties(entry.MarkerFormatName);
                    if (properties == null)
                    {
                        continue;
                    }

                    properties[EditorFormatDefinition.BackgroundColorId] =
                        Color.FromArgb(alpha, entry.Highlight.R, entry.Highlight.G, entry.Highlight.B);

                    // The border tracks the fill so it does not end up outlining a highlight that
                    // has been faded almost to nothing.
                    properties[EditorFormatDefinition.ForegroundColorId] =
                        Color.FromArgb(BorderAlphaFor(alpha), entry.Border.R, entry.Border.G, entry.Border.B);

                    map.SetProperties(entry.MarkerFormatName, properties);
                }
            }
            catch (Exception ex)
            {
                LocalComentsLog.Write($"Could not apply the highlight opacity: {ex.Message}");
            }
            finally
            {
                try
                {
                    map.EndBatchUpdate();
                }
                catch (Exception ex)
                {
                    LocalComentsLog.Write($"Could not commit the highlight opacity: {ex.Message}");
                }
            }
        }

        private static IEditorFormatMap? TryGetFormatMap()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var componentModel = ServiceProvider.GlobalProvider.GetService(typeof(SComponentModel)) as IComponentModel;
            return componentModel?.GetService<IEditorFormatMapService>()?.GetEditorFormatMap(TextCategory);
        }

        private static byte ToAlpha(int percent)
        {
            var clamped = Math.Max(MinimumPercent, Math.Min(MaximumPercent, percent));
            return (byte)Math.Round(clamped * 255.0 / 100.0);
        }

        private static int ToPercent(byte alpha)
            => (int)Math.Round(alpha * 100.0 / 255.0);

        /// <summary>Border stays somewhat stronger than the fill, but fades with it.</summary>
        private static byte BorderAlphaFor(byte fillAlpha)
            => (byte)Math.Min(255, fillAlpha * 2);
    }
}
