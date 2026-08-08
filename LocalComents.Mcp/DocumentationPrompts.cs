using System.ComponentModel;
using ModelContextProtocol.Server;

namespace LocalComents.Mcp
{
    /// <summary>
    /// Reusable prompt templates. In Visual Studio these show up under
    /// <c>+ Add Reference &gt; Prompts &gt; MCP prompts</c> in the chat pane.
    /// </summary>
    [McpServerPromptType]
    internal static class DocumentationPrompts
    {
        [McpServerPrompt(Name = "generate_documentation")]
        [Description("Turns the local comments of the project into a Markdown document with Mermaid diagrams, and writes it to disk.")]
        public static string GenerateDocumentation(
            [Description("Optional subject to focus on, e.g. 'authentication' or 'the payment module'. Leave empty to cover the whole project.")]
            string? focus = null)
        {
            var scope = string.IsNullOrWhiteSpace(focus)
                ? "Cover the whole project."
                : $"Focus on: {focus!.Trim()}. Use search_comments to narrow down to the relevant annotations.";

            return $"""
                Write technical documentation for this project based on its local comments.

                {scope}

                Steps:
                1. Call `list_files_with_comments` to see where the annotations are concentrated.
                2. Call `get_comments` (or `search_comments` when scoped) to read them. Each comment
                   carries the text I wrote, the line it is anchored to and the code snippet it refers to.
                3. Read the actual source files for any area the comments point at — the comments are
                   my notes and shorthand, not a specification. Treat them as signals about what matters,
                   then verify against the code before describing behaviour.
                4. Produce a Markdown document with:
                   - a short overview of what the project does;
                   - one section per area or module, explaining decisions, caveats and open questions
                     the comments raise, each with a `file.cs:line` reference;
                   - at least one Mermaid diagram in a ```mermaid fenced block. Choose the type that
                     actually fits what the comments describe: `flowchart` for control flow,
                     `sequenceDiagram` for interaction between components, `classDiagram` for structure.
                     Do not force a diagram onto content that is not structural.
                5. Call `write_documentation` with the finished Markdown.

                Do not invent architecture that is not evidenced by the comments or the code. If the
                comments are too sparse to document an area, say so explicitly instead of filling the gap.
                """;
        }

        [McpServerPrompt(Name = "review_open_questions")]
        [Description("Collects the local comments that read as doubts, TODOs or warnings and turns them into a prioritised action list.")]
        public static string ReviewOpenQuestions()
        {
            return """
                Go through the local comments in this project and pull out the ones that represent
                unfinished work: doubts, TODOs, warnings, notes about temporary workarounds or
                things I flagged as needing a second look.

                Use `get_comments` to read them all, then read the surrounding code for each one you
                select so you can judge whether it is still relevant — some notes go stale once the
                code around them changes.

                Return a prioritised list. For each item: the `file.cs:line` reference, my original
                note, what the code does today, and whether it still applies. Sort by how much damage
                the issue could cause, not by file order. Say plainly when a note appears to be
                already resolved.
                """;
        }
    }
}
