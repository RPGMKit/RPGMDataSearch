using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace RPGMDataSearch;

/// <summary>
/// The field of the script: a box of several lines with a list of suggestions (ScriptCompletion). The list opens when a dot is typed or with Ctrl+Space, follows what is typed,
/// and is chosen from with Up, Down, Enter or Tab (Escape closes it). Ctrl+Enter asks for the search (Enter is a new line).
/// </summary>
internal sealed class ScriptEditor
{
    private readonly Popup _popup;
    private readonly ListBox _list;
    private bool _forced;
    private bool _inserting;
    private int _start;

    public TextBox Box { get; }

    public event Action? SearchRequested;

    public ScriptEditor()
    {
        Box = new TextBox
        {
            Width = 820, Height = 84, Padding = new Thickness(2), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas"),
            ToolTip = "An expression that is true for the commands to find. command (NamedCommand), line, source, location. A dot shows the members, Ctrl+Space the names, Ctrl+Enter searches.",
        };

        _list = new ListBox { MaxHeight = 240, MinWidth = 360, FontFamily = new FontFamily("Consolas"), Focusable = false };
        _list.MouseDoubleClick += (_, _) => Accept();
        _popup = new Popup
        {
            Child = new Border { Child = _list, BorderThickness = new Thickness(1), BorderBrush = Brushes.Gray, Background = SystemColors.WindowBrush },
            PlacementTarget = Box, Placement = PlacementMode.Relative, StaysOpen = false,
        };
        _popup.Closed += (_, _) => _forced = false;

        Box.TextChanged += (_, _) => OnTextChanged();
        Box.PreviewKeyDown += OnKeyDown;
        Box.LostKeyboardFocus += (_, _) => Close();
    }

    private bool IsOpen => _popup.IsOpen;

    private void Close() => _popup.IsOpen = false;

    private void OnTextChanged()
    {
        if (_inserting)
            return;

        var caret = Box.CaretIndex;
        // a dot that was just typed opens the list; an open list follows the word
        var typedDot = caret > 0 && Box.Text.Length >= caret && Box.Text[caret - 1] == '.';
        if (typedDot || IsOpen)
            Show();
    }

    private void Show()
    {
        if (ScriptCompletion.Complete(Box.Text, Box.CaretIndex, _forced) is not { } found)
        {
            Close();
            return;
        }

        _start = found.Start;
        _list.Items.Clear();
        foreach (var item in found.Items)
            _list.Items.Add(new ListBoxItem { Content = item.Detail.Length > 0 ? $"{item.Name}   {item.Detail}" : item.Name, Tag = item.Name });

        _list.SelectedIndex = 0;
        var rect = Box.GetRectFromCharacterIndex(Box.CaretIndex, true);
        _popup.HorizontalOffset = rect.IsEmpty ? 0 : rect.Left;
        _popup.VerticalOffset = rect.IsEmpty ? Box.ActualHeight : rect.Bottom + 2;
        _popup.IsOpen = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        if (ctrl && e.Key == Key.Space)
        {
            _forced = true;
            Show();
            e.Handled = true;
            return;
        }

        if (ctrl && e.Key == Key.Enter)
        {
            Close();
            e.Handled = true;
            SearchRequested?.Invoke();
            return;
        }

        if (!IsOpen)
            return;

        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                _list.ScrollIntoView(_list.SelectedItem);
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab:
                Accept();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    // the word that is typed is replaced by the chosen name
    private void Accept()
    {
        if (_list.SelectedItem is not ListBoxItem { Tag: string name })
            return;

        _inserting = true;
        try
        {
            var caret = Box.CaretIndex;
            Box.Select(_start, caret - _start);
            Box.SelectedText = name;
            Box.CaretIndex = _start + name.Length;
        }
        finally
        {
            _inserting = false;
        }

        Close();
        Box.Focus();
    }
}
