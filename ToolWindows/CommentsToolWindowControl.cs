using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LocalComents.Editor;
using LocalComents.Models;
using LocalComents.Services;
using LocalComents.UI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace LocalComents.ToolWindows
{
    /// <summary>
    /// Lists every comment in the current storage file with search, navigation, inline edit
    /// and delete. Colours come from the shell theme so the pane follows Dark/Light/Blue.
    /// </summary>
    public sealed class CommentsToolWindowControl : UserControl
    {
        private readonly TextBox _search;
        private readonly StackPanel _list;
        private readonly TextBlock _status;

        public CommentsToolWindowControl()
        {
            this.SetResourceReference(BackgroundProperty, VsBrushes.ToolWindowBackgroundKey);
            this.SetResourceReference(ForegroundProperty, VsBrushes.ToolWindowTextKey);
            FontSize = 12;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new DockPanel { Margin = new Thickness(8, 8, 8, 6), LastChildFill = true };

            var refresh = ThemedButton("Refresh", "Reload the comments file from disk");
            refresh.Margin = new Thickness(6, 0, 0, 0);
            refresh.Click += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                CommentStore.Instance.Reload();
                Rebuild();
            };
            DockPanel.SetDock(refresh, Dock.Right);
            header.Children.Add(refresh);

            _search = new TextBox { ToolTip = "Search in comments, code snippets and file names", Padding = new Thickness(2) };
            _search.SetResourceReference(StyleProperty, VsResourceKeys.TextBoxStyleKey);
            _search.TextChanged += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Rebuild();
            };
            header.Children.Add(_search);

            Grid.SetRow(header, 0);
            root.Children.Add(header);

            _list = new StackPanel { Margin = new Thickness(8, 0, 8, 8) };
            var scroller = new ScrollViewer
            {
                Content = _list,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };
            Grid.SetRow(scroller, 1);
            root.Children.Add(scroller);

            _status = new TextBlock
            {
                Margin = new Thickness(8, 0, 8, 6),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
            };
            _status.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            Grid.SetRow(_status, 2);
            root.Children.Add(_status);

            Content = root;

            CommentStore.Instance.CommentsChanged += OnCommentsChanged;
            Unloaded += (_, _) => CommentStore.Instance.CommentsChanged -= OnCommentsChanged;
            Loaded += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Rebuild();
            };
        }

        private void OnCommentsChanged(object sender, CommentsChangedEventArgs e)
        {
            // Refreshing the pane is fire-and-forget on purpose: nothing waits on it, and a
            // failure must not propagate back into the store's file watcher.
#pragma warning disable VSSDK007
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                Rebuild();
            }).FileAndForget("LocalComents/ToolWindow/Rebuild");
#pragma warning restore VSSDK007
        }

        private void Rebuild()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _list.Children.Clear();

            var filter = _search.Text?.Trim() ?? string.Empty;
            var files = CommentStore.Instance.GetAll()
                .OrderBy(pair => Path.GetFileName(pair.Key), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = 0;
            var shown = 0;

            foreach (var file in files)
            {
                var matches = file.Value
                    .Where(c => Matches(file.Key, c, filter))
                    // Newest first, mirroring the VS Code sidebar ordering.
                    .OrderByDescending(c => c.Timestamp)
                    .ToList();

                total += file.Value.Count;
                if (matches.Count == 0)
                {
                    continue;
                }

                shown += matches.Count;
                _list.Children.Add(BuildFileHeader(file.Key, matches.Count));

                foreach (var comment in matches)
                {
                    _list.Children.Add(BuildRow(file.Key, comment));
                }
            }

            if (total == 0)
            {
                _status.Text = $"No comments yet — press Alt+C in the editor.   File: {CommentStore.Instance.StoragePath}";
            }
            else
            {
                _status.Text = $"{shown} of {total} comment(s) in {files.Count} file(s).   File: {CommentStore.Instance.StoragePath}";
            }
        }

        private static bool Matches(string filePath, LocalComment comment, string filter)
        {
            if (string.IsNullOrEmpty(filter))
            {
                return true;
            }

            return Contains(comment.Text, filter)
                || Contains(comment.Range?.SelectedText, filter)
                || Contains(filePath, filter);
        }

        private static bool Contains(string? value, string filter)
            => !string.IsNullOrEmpty(value) && value!.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;

        private UIElement BuildFileHeader(string filePath, int count)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 5) };

            var name = new TextBlock
            {
                Text = Path.GetFileName(filePath),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
            };
            name.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            panel.Children.Add(name);

            var folder = new TextBlock
            {
                Text = $"  {DisplayFolder(filePath)}  ({count})",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = filePath,
            };
            folder.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            panel.Children.Add(folder);

            return panel;
        }

        private UIElement BuildRow(string filePath, LocalComment comment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var range = comment.Range ?? new CommentRange();

            // The pane is colour-coded the same way the editor is, so a comment is recognisable
            // in both places without reading it.
            var accent = CommentPalette.Resolve(comment.Color).Highlight;

            var texts = new StackPanel();

            var body = new TextBlock
            {
                Text = comment.Text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
            };
            body.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.ToolWindowTextKey);
            texts.Children.Add(body);

            // The code the comment was anchored to, so the entry is readable without opening the file.
            var snippet = range.SelectedText;
            if (!string.IsNullOrWhiteSpace(snippet))
            {
                var code = new TextBlock
                {
                    Text = Flatten(snippet!),
                    FontFamily = new FontFamily("Consolas, Cascadia Mono, Courier New"),
                    FontSize = 11,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Padding = new Thickness(6, 3, 6, 3),
                };
                code.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

                texts.Children.Add(new Border
                {
                    Child = code,
                    Margin = new Thickness(0, 5, 0, 0),
                    CornerRadius = new CornerRadius(2),
                    Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
                    BorderThickness = new Thickness(2, 0, 0, 0),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B)),
                });
            }

            var reference = new TextBlock
            {
                Text = BuildReference(filePath, comment),
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = $"{filePath}:{range.StartLine + 1}",
            };
            reference.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            texts.Children.Add(reference);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(texts, 0);
            grid.Children.Add(texts);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };

            var edit = ThemedButton("Edit", "Change the comment text");
            edit.Margin = new Thickness(6, 0, 0, 0);
            edit.Click += (_, _) => EditComment(filePath, comment);
            buttons.Children.Add(edit);

            var delete = ThemedButton("Delete", "Remove this comment");
            delete.Margin = new Thickness(4, 0, 0, 0);
            delete.Click += (_, _) => DeleteComment(filePath, comment);
            buttons.Children.Add(delete);

            Grid.SetColumn(buttons, 1);
            grid.Children.Add(buttons);

            var container = new Border
            {
                Child = grid,
                Padding = new Thickness(9, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 5),
                CornerRadius = new CornerRadius(3),
                BorderThickness = new Thickness(3, 1, 1, 1),
                BorderBrush = new SolidColorBrush(accent),
                Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128)),
                Cursor = Cursors.Hand,
                ToolTip = "Click to go to this line",
            };

            container.MouseEnter += (_, _) =>
                container.Background = new SolidColorBrush(Color.FromArgb(38, accent.R, accent.G, accent.B));
            container.MouseLeave += (_, _) =>
                container.Background = new SolidColorBrush(Color.FromArgb(18, 128, 128, 128));

            container.MouseLeftButtonUp += (_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                Navigate(filePath, comment);
            };

            return container;
        }

        /// <summary>"Program.cs:13 · src\App · 08/08/2026 12:17" — where the comment was taken.</summary>
        private static string BuildReference(string filePath, LocalComment comment)
        {
            var parts = new System.Collections.Generic.List<string>
            {
                $"{Path.GetFileName(filePath)}:{comment.Range.StartLine + 1}",
            };

            if (comment.Range.EndLine > comment.Range.StartLine)
            {
                parts[0] += $"-{comment.Range.EndLine + 1}";
            }
            else if (!comment.Range.IsWholeLine)
            {
                parts[0] += $", col {comment.Range.StartCharacter + 1}";
            }

            var folder = DisplayFolder(filePath);
            if (!string.IsNullOrEmpty(folder))
            {
                parts.Add(folder);
            }

            if (comment.Timestamp > 0)
            {
                parts.Add(comment.CreatedAtLocal.ToString("g"));
            }

            return string.Join("  ·  ", parts);
        }

        /// <summary>Folder of the file, relative to the storage folder when possible.</summary>
        private static string DisplayFolder(string filePath)
        {
            var folder = Path.GetDirectoryName(filePath) ?? string.Empty;
            var storage = CommentStore.Instance.StoragePath;
            var root = string.IsNullOrEmpty(storage) ? null : Path.GetDirectoryName(storage);

            if (!string.IsNullOrEmpty(root)
                && folder.StartsWith(root!, StringComparison.OrdinalIgnoreCase))
            {
                var relative = folder.Substring(root!.Length).TrimStart(Path.DirectorySeparatorChar);
                return relative.Length == 0 ? "." : relative;
            }

            return folder;
        }

        private static Button ThemedButton(string content, string tooltip)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 58,
                Padding = new Thickness(8, 2, 8, 2),
                ToolTip = tooltip,
            };
            button.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            return button;
        }

        private static string Flatten(string value)
            => value.Replace("\r", " ").Replace("\n", " ").Trim();

        private void Navigate(string filePath, LocalComment comment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (!File.Exists(filePath))
            {
                return;
            }

            EditorAccess.NavigateTo(
                ServiceProvider.GlobalProvider,
                filePath,
                comment.Range.StartLine,
                comment.Range.StartCharacter);
        }

        private void EditComment(string filePath, LocalComment comment)
        {
            var input = CommentInputDialog.Prompt(
                "Edit local comment",
                comment.Range?.SelectedText,
                comment.Text,
                comment.Color);

            if (input != null)
            {
                CommentStore.Instance.Update(filePath, comment.Id, input.Text, input.ColorId);
            }
        }

        private void DeleteComment(string filePath, LocalComment comment)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // VsShellUtilities rather than System.Windows.MessageBox: a WPF message box raised
            // without an owner is not parented to the shell, so it can open behind the IDE. The
            // click then looks like it did nothing, because the prompt nobody answered is what
            // the deletion is waiting on.
            var answer = VsShellUtilities.ShowMessageBox(
                ServiceProvider.GlobalProvider,
                "Delete this local comment?",
                "Local Comments",
                OLEMSGICON.OLEMSGICON_QUERY,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);

            if (answer == (int)VSConstants.MessageBoxResult.IDYES)
            {
                CommentStore.Instance.Remove(filePath, comment.Id);
            }
        }
    }
}
