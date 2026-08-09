using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using LocalComents.Editor;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;

namespace LocalComents.UI
{
    /// <summary>What the user typed and picked, or <c>null</c> from the prompt when cancelled.</summary>
    public sealed class CommentInput
    {
        public CommentInput(string text, string? colorId)
        {
            Text = text;
            ColorId = colorId;
        }

        public string Text { get; }

        /// <summary>Palette identifier, or <c>null</c> for the default colour.</summary>
        public string? ColorId { get; }
    }

    /// <summary>
    /// Themed prompt used to create or edit a comment. Built in code so the project keeps a
    /// single source of truth for the dialog and avoids a XAML/code-behind pair.
    /// </summary>
    public sealed class CommentInputDialog : DialogWindow
    {
        private readonly TextBox _input;
        private string _colorId;

        public CommentInputDialog(string title, string? anchorText, string initialText = "", string? initialColorId = null)
        {
            // The opacity control reads the editor's format map, which is main-thread only.
            ThreadHelper.ThrowIfNotOnUIThread();

            _colorId = CommentPalette.Resolve(initialColorId).Id;

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

            // The window's own background, not just the content's: the root Grid is inset by 12px,
            // and without this the default white shows through as a border around the themed
            // content — which is what made the dialog look foreign to the IDE.
            SetResourceReference(BackgroundProperty, VsBrushes.WindowKey);
            SetResourceReference(ForegroundProperty, VsBrushes.WindowTextKey);

            var root = new Grid { Margin = new Thickness(12) };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

            var picker = BuildPalettePicker();
            picker.Children.Add(BuildOpacityControl());
            Grid.SetRow(picker, 2);
            root.Children.Add(picker);

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

            Grid.SetRow(footer, 3);
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

        /// <summary>Shows the dialog and returns what was entered, or <c>null</c> when cancelled.</summary>
        public static CommentInput? Prompt(
            string title,
            string? anchorText,
            string initialText = "",
            string? initialColorId = null)
        {
            var dialog = new CommentInputDialog(title, anchorText, initialText, initialColorId);
            if (dialog.ShowModal() == true && !string.IsNullOrWhiteSpace(dialog.CommentText))
            {
                return new CommentInput(dialog.CommentText, CommentPalette.ToStoredValue(dialog._colorId));
            }

            return null;
        }

        /// <summary>
        /// A row of swatches, one per palette entry. Radio buttons rather than hand-rolled
        /// clickable borders so arrow-key navigation and screen readers work without extra code.
        /// </summary>
        /// <summary>
        /// Strength of the code highlight. Global by nature — see <see cref="HighlightOpacity"/> —
        /// so the label says so rather than letting it read as a property of this comment.
        /// </summary>
        private UIElement BuildOpacityControl()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(14, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var label = new TextBlock
            {
                Text = "Opacity (all):",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            panel.Children.Add(label);

            var current = HighlightOpacity.GetPercent();

            var value = new TextBlock
            {
                Text = current + "%",
                MinWidth = 34,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            value.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);

            var slider = new Slider
            {
                Minimum = HighlightOpacity.MinimumPercent,
                Maximum = HighlightOpacity.MaximumPercent,
                Value = current,
                Width = 120,
                // Snapped to 5% steps so dragging applies a handful of updates to the format map
                // rather than one per pixel.
                TickFrequency = 5,
                IsSnapToTickEnabled = true,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "How strongly the highlight is painted behind the code, for every comment",
            };

            slider.ValueChanged += (_, e) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                var percent = (int)Math.Round(e.NewValue);
                value.Text = percent + "%";
                HighlightOpacity.SetPercent(percent);
            };

            panel.Children.Add(slider);
            panel.Children.Add(value);

            return panel;
        }

        private StackPanel BuildPalettePicker()
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0),
            };

            var label = new TextBlock
            {
                Text = "Color:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0),
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            row.Children.Add(label);

            foreach (var entry in CommentPalette.Entries)
            {
                var swatch = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(3),
                    Background = new SolidColorBrush(entry.Highlight),
                    BorderBrush = new SolidColorBrush(entry.Border),
                    BorderThickness = new Thickness(1),
                };

                var option = new RadioButton
                {
                    Content = swatch,
                    GroupName = "LocalComentsCommentColor",
                    ToolTip = entry.DisplayName,
                    Tag = entry.Id,
                    IsChecked = entry.Id == _colorId,
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0),
                };
                AutomationProperties.SetName(option, entry.DisplayName);
                option.Checked += (sender, _) => _colorId = (string)((RadioButton)sender).Tag;

                row.Children.Add(option);
            }

            return row;
        }

        private static string Truncate(string value, int max)
            => value.Length <= max ? value : value.Substring(0, max) + "…";

        private static string Flatten(string value)
            => value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
