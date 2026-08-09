using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace LocalComents.UI
{
    /// <summary>
    /// Themed prompt used to create or edit a comment. Built in code so the project keeps a
    /// single source of truth for the dialog and avoids a XAML/code-behind pair.
    /// </summary>
    public sealed class CommentInputDialog : DialogWindow
    {
        private readonly TextBox _input;

        public CommentInputDialog(string title, string? anchorText, string initialText = "")
        {
            Title = title;

            // Comments run to a couple of sentences, so the box is sized for prose and stays
            // resizable. A Grid rather than a StackPanel is what makes resizing worth anything:
            // in a StackPanel the text box keeps its minimum height and the extra space goes
            // nowhere.
            Width = 620;
            Height = 340;
            MinWidth = 420;
            MinHeight = 260;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            HasMaximizeButton = false;
            HasMinimizeButton = false;

            var root = new Grid { Margin = new Thickness(12) };
            // Panel.BackgroundProperty, not the inherited Control.BackgroundProperty: they are
            // distinct dependency properties and a Grid paints itself from the former.
            root.SetResourceReference(Panel.BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            if (!string.IsNullOrWhiteSpace(anchorText))
            {
                var flattened = Flatten(anchorText!);
                var anchor = new TextBlock
                {
                    Text = Truncate(flattened, 160),
                    ToolTip = flattened,
                    FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono, Courier New"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                anchor.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
                Grid.SetRow(anchor, 0);
                root.Children.Add(anchor);
            }

            _input = new TextBox
            {
                Text = initialText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalContentAlignment = VerticalAlignment.Top,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(6, 4, 6, 4),
            };
            _input.SetResourceReference(StyleProperty, VsResourceKeys.TextBoxStyleKey);
            Grid.SetRow(_input, 1);
            root.Children.Add(_input);

            var footer = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var hint = new TextBlock
            {
                Text = "Ctrl+Enter to save  ·  Esc to cancel",
                VerticalAlignment = VerticalAlignment.Center,
            };
            hint.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            Grid.SetColumn(hint, 0);
            footer.Children.Add(hint);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal };

            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            ok.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            ok.Click += (_, _) => { DialogResult = true; Close(); };

            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            cancel.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            cancel.Click += (_, _) => { DialogResult = false; Close(); };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.SetColumn(buttons, 1);
            footer.Children.Add(buttons);

            Grid.SetRow(footer, 2);
            root.Children.Add(footer);

            Content = root;

            // A TextBox with AcceptsReturn handles Enter itself, so the default button never fires
            // while the caret is in the box. Ctrl+Enter is the accelerator that does.
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    DialogResult = true;
                    Close();
                    e.Handled = true;
                }
            };

            Loaded += (_, _) =>
            {
                _input.Focus();

                // Caret at the end, not SelectAll: when editing an existing comment the first
                // keystroke would otherwise wipe what is already there.
                _input.CaretIndex = _input.Text.Length;
                _input.ScrollToEnd();
            };
        }

        public string CommentText => _input.Text?.Trim() ?? string.Empty;

        /// <summary>Shows the dialog and returns the typed text, or <c>null</c> when cancelled.</summary>
        public static string? Prompt(string title, string? anchorText, string initialText = "")
        {
            var dialog = new CommentInputDialog(title, anchorText, initialText);
            if (dialog.ShowModal() == true && !string.IsNullOrWhiteSpace(dialog.CommentText))
            {
                return dialog.CommentText;
            }

            return null;
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value.Substring(0, max) + "…";

        private static string Flatten(string value)
            => value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
