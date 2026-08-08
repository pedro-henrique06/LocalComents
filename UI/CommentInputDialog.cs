using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace LocalComents.UI
{
    /// <summary>
    /// Small themed prompt used to create or edit a comment. Built in code so the project
    /// keeps a single source of truth for the dialog and avoids a XAML/code-behind pair.
    /// </summary>
    public sealed class CommentInputDialog : DialogWindow
    {
        private readonly TextBox _input;

        public CommentInputDialog(string title, string? anchorText, string initialText = "")
        {
            Title = title;
            Width = 460;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.NoResize;
            HasMaximizeButton = false;
            HasMinimizeButton = false;

            var root = new StackPanel { Margin = new Thickness(12) };
            root.SetResourceReference(StackPanel.BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            if (!string.IsNullOrWhiteSpace(anchorText))
            {
                var anchor = new TextBlock
                {
                    Text = Truncate(Flatten(anchorText!), 120),
                    FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono, Courier New"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                anchor.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
                root.Children.Add(anchor);
            }

            _input = new TextBox
            {
                Text = initialText,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 70,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            };
            _input.SetResourceReference(StyleProperty, VsResourceKeys.TextBoxStyleKey);
            root.Children.Add(_input);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
            };

            var ok = new Button { Content = "OK", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
            ok.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            ok.Click += (_, _) => { DialogResult = true; Close(); };

            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            cancel.SetResourceReference(StyleProperty, VsResourceKeys.ButtonStyleKey);
            cancel.Click += (_, _) => { DialogResult = false; Close(); };

            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);

            Content = root;

            // Ctrl+Enter also confirms, since Enter inserts a new line.
            _input.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    DialogResult = true;
                    Close();
                }
            };

            Loaded += (_, _) =>
            {
                _input.Focus();
                _input.SelectAll();
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
