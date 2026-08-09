using System;
using Newtonsoft.Json;

namespace LocalComents.Models
{
    /// <summary>
    /// A single annotation attached to a range of a file. The JSON shape is intentionally
    /// identical to the VS Code "Local Comments" extension so the same storage file can be
    /// shared between both editors.
    /// </summary>
    public sealed class LocalComment
    {
        [JsonProperty("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        [JsonProperty("text")]
        public string Text { get; set; } = string.Empty;

        /// <summary>Unix time in milliseconds, matching the VS Code extension.</summary>
        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("range")]
        public CommentRange Range { get; set; } = new CommentRange();

        /// <summary>
        /// Palette entry this comment is drawn with, or <c>null</c> for the default. Kept as a
        /// plain identifier rather than an RGB value so the meaning survives a theme change, and
        /// so this file stays free of UI types — it is shared with the MCP server project.
        /// <para>
        /// An extra property is ignored by the VS Code extension when it reads the file, but it
        /// is dropped if VS Code rewrites that comment.
        /// </para>
        /// </summary>
        [JsonProperty("color")]
        public string? Color { get; set; }

        [JsonIgnore]
        public DateTime CreatedAtLocal =>
            Timestamp <= 0
                ? DateTime.MinValue
                : DateTimeOffset.FromUnixTimeMilliseconds(Timestamp).LocalDateTime;

        public static long NowTimestamp() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>
    /// Zero-based range, matching VS Code semantics. A whole-line comment is stored with
    /// <see cref="EndCharacter"/> set to <see cref="WholeLineEndCharacter"/>.
    /// </summary>
    public sealed class CommentRange
    {
        /// <summary>JavaScript's Number.MAX_SAFE_INTEGER, used by VS Code for "to end of line".</summary>
        public const long WholeLineEndCharacter = 9007199254740991L;

        [JsonProperty("startLine")]
        public int StartLine { get; set; }

        [JsonProperty("startCharacter")]
        public int StartCharacter { get; set; }

        [JsonProperty("endLine")]
        public int EndLine { get; set; }

        [JsonProperty("endCharacter")]
        public long EndCharacter { get; set; }

        [JsonProperty("selectedText")]
        public string? SelectedText { get; set; }

        [JsonIgnore]
        public bool IsWholeLine => EndCharacter >= int.MaxValue;
    }
}
