# Marketplace listing

Source of truth for the **Overview** field of the Visual Studio Marketplace listing.
Paste the content below the separator into the Overview editor when publishing or updating.

Written in English on purpose: the Marketplace audience is international, and the
`<Description>` in the manifest is already English.

---

Keep notes on your code without touching the source files. Press `Alt+C` on any line and write
what you are thinking — the annotation lives in a JSON file beside your solution, never in the
code itself. Nothing to strip before committing, nothing leaking into a pull request.

## What it does

| | |
| --- | --- |
| **Annotate a line or a selection** | `Alt+C` in the editor, or right-click → *Add Local Comment* |
| **See it inline** | The note is drawn at the end of the line, like an inline hint |
| **Highlight and gutter marker** | The annotated code is highlighted and flagged in the margin |
| **Hover for detail** | Quick Info shows the note, when you wrote it, and warns when the code changed underneath |
| **Browse everything** | *View → Other Windows → Local Comments*: search, jump, edit, delete |

## Shared with VS Code

Comments are stored in the same JSON format as the
[Local Comments](https://github.com/marcelrsoub/local-comments) extension for VS Code, so a single
`.local-comments.json` works across both editors on the same repository. The file is watched, so
edits made in one editor show up in the other.

## Ask Copilot about your notes

The extension bundles an MCP server and registers it for the open solution. In **Agent mode**, the
Copilot chat can read your annotations and act on them — no API key, no extra install.

Two ready-made prompts ship with it:

- **`generate_documentation`** — reads your notes, checks them against the actual source, and
  writes a Markdown document with a Mermaid diagram.
- **`review_open_questions`** — collects the TODOs and doubts you left behind into a prioritised
  action list.

Enable the tools once in the chat's tool picker (Visual Studio ships every MCP server disabled by
default) and ask away.

## Configuration

*Tools → Options → Local Comments*: where the file is stored (solution, user profile or a custom
folder), which visual indicators to show, whether to hide comments whose anchor code has changed,
and whether to register the MCP server.

The highlight colour is a normal editor format — change it under
*Tools → Options → Environment → Fonts and Colors → Local Comments Highlight*.

## Requirements

Visual Studio 2022 17.14 or later, including Visual Studio 2026.

Source and issues: https://github.com/pedro-henrique06/LocalComents
