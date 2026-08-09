using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Windows.Media;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

namespace LocalComents.Editor
{
    /// <summary>One selectable comment colour.</summary>
    internal sealed class CommentPaletteEntry
    {
        private TextMarkerTag? _markerTag;

        public CommentPaletteEntry(string id, string displayName, Color highlight, Color border, string markerFormatName)
        {
            Id = id;
            DisplayName = displayName;
            Highlight = highlight;
            Border = border;
            MarkerFormatName = markerFormatName;
        }

        /// <summary>Value written to the JSON file. Stable across themes and releases.</summary>
        public string Id { get; }

        public string DisplayName { get; }

        public Color Highlight { get; }

        public Color Border { get; }

        /// <summary>Name of the <see cref="MarkerFormatDefinition"/> that paints this colour.</summary>
        public string MarkerFormatName { get; }

        public TextMarkerTag MarkerTag => _markerTag ??= new TextMarkerTag(MarkerFormatName);
    }

    /// <summary>
    /// The fixed set of colours a comment can be given.
    /// <para>
    /// Fixed rather than free-form RGB because the highlight is drawn through
    /// <see cref="TextMarkerTag"/>, which addresses a <see cref="MarkerFormatDefinition"/> by
    /// name. A named definition per colour is also what puts each one under
    /// <em>Tools > Options > Environment > Fonts and Colors</em>, so the palette stays editable.
    /// </para>
    /// </summary>
    internal static class CommentPalette
    {
        public const string DefaultId = "yellow";

        // The default keeps the original format name so any customisation of it survives.
        public static readonly CommentPaletteEntry Yellow = new CommentPaletteEntry(
            DefaultId, "Yellow", Color.FromRgb(0xF5, 0xD1, 0x76), Color.FromRgb(0xC9, 0x94, 0x18), "LocalComents.CommentMarker");

        public static readonly CommentPaletteEntry Orange = new CommentPaletteEntry(
            "orange", "Orange", Color.FromRgb(0xF0, 0xA8, 0x68), Color.FromRgb(0xC9, 0x70, 0x1A), "LocalComents.CommentMarker.Orange");

        public static readonly CommentPaletteEntry Red = new CommentPaletteEntry(
            "red", "Red", Color.FromRgb(0xEF, 0x9A, 0x9A), Color.FromRgb(0xC6, 0x28, 0x28), "LocalComents.CommentMarker.Red");

        public static readonly CommentPaletteEntry Green = new CommentPaletteEntry(
            "green", "Green", Color.FromRgb(0xA5, 0xD6, 0xA7), Color.FromRgb(0x2E, 0x7D, 0x32), "LocalComents.CommentMarker.Green");

        public static readonly CommentPaletteEntry Blue = new CommentPaletteEntry(
            "blue", "Blue", Color.FromRgb(0x9E, 0xC5, 0xFE), Color.FromRgb(0x15, 0x65, 0xC0), "LocalComents.CommentMarker.Blue");

        public static readonly CommentPaletteEntry Purple = new CommentPaletteEntry(
            "purple", "Purple", Color.FromRgb(0xC5, 0xA3, 0xE0), Color.FromRgb(0x6A, 0x1B, 0x9A), "LocalComents.CommentMarker.Purple");

        public static IReadOnlyList<CommentPaletteEntry> Entries { get; } =
            new[] { Yellow, Orange, Red, Green, Blue, Purple };

        /// <summary>
        /// Maps a stored identifier to its entry, falling back to the default. Unknown values are
        /// tolerated on purpose: the file may have been written by a newer version, and losing the
        /// colour is better than losing the comment.
        /// </summary>
        public static CommentPaletteEntry Resolve(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return Yellow;
            }

            return Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? Yellow;
        }

        /// <summary>The value to persist — <c>null</c> for the default, so untouched files stay untouched.</summary>
        public static string? ToStoredValue(string? id)
            => string.Equals(id, DefaultId, StringComparison.OrdinalIgnoreCase) ? null : id;
    }

    /// <summary>Shared shape of the per-colour marker definitions below.</summary>
    internal abstract class CommentMarkerFormat : MarkerFormatDefinition
    {
        protected CommentMarkerFormat(CommentPaletteEntry entry)
        {
            BackgroundColor = entry.Highlight;
            ForegroundColor = entry.Border;
            DisplayName = "Local Comments Highlight - " + entry.DisplayName;
            ZOrder = 5;
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("LocalComents.CommentMarker")]
    [UserVisible(true)]
    internal sealed class YellowCommentMarkerFormat : CommentMarkerFormat
    {
        public YellowCommentMarkerFormat()
            : base(CommentPalette.Yellow)
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("LocalComents.CommentMarker.Orange")]
    [UserVisible(true)]
    internal sealed class OrangeCommentMarkerFormat : CommentMarkerFormat
    {
        public OrangeCommentMarkerFormat()
            : base(CommentPalette.Orange)
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("LocalComents.CommentMarker.Red")]
    [UserVisible(true)]
    internal sealed class RedCommentMarkerFormat : CommentMarkerFormat
    {
        public RedCommentMarkerFormat()
            : base(CommentPalette.Red)
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("LocalComents.CommentMarker.Green")]
    [UserVisible(true)]
    internal sealed class GreenCommentMarkerFormat : CommentMarkerFormat
    {
        public GreenCommentMarkerFormat()
            : base(CommentPalette.Green)
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("LocalComents.CommentMarker.Blue")]
    [UserVisible(true)]
    internal sealed class BlueCommentMarkerFormat : CommentMarkerFormat
    {
        public BlueCommentMarkerFormat()
            : base(CommentPalette.Blue)
        {
        }
    }

    [Export(typeof(EditorFormatDefinition))]
    [Name("LocalComents.CommentMarker.Purple")]
    [UserVisible(true)]
    internal sealed class PurpleCommentMarkerFormat : CommentMarkerFormat
    {
        public PurpleCommentMarkerFormat()
            : base(CommentPalette.Purple)
        {
        }
    }
}
