using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;
using RPGMDataKit.Storages.Abstraction;

namespace RPGMDataSearch;

public partial class MainWindow : Window
{
    private enum Shape
    {
        Ids,
        Letters,
        Text,
        Script,
    }

    // the tabs of the search: what is found, the title and the controls the tab has
    private static readonly (SearchKind Kind, string Title, Shape Shape, bool HasAccess)[] Kinds =
    [
        (SearchKind.CommonEventCalls, "Common event calls", Shape.Ids, false),
        (SearchKind.Pictures, "Show Picture", Shape.Text, false),
        (SearchKind.Switches, "Switches", Shape.Ids, true),
        (SearchKind.Variables, "Variables", Shape.Ids, true),
        (SearchKind.SelfSwitches, "Self switches", Shape.Letters, true),
        (SearchKind.Items, "Items", Shape.Ids, true),
        (SearchKind.Weapons, "Weapons", Shape.Ids, true),
        (SearchKind.Armors, "Armors", Shape.Ids, true),
        (SearchKind.Actors, "Actors", Shape.Ids, true),
        (SearchKind.TransferMap, "Transfer map", Shape.Ids, false),
        (SearchKind.CommandText, "Command text", Shape.Text, false),
        (SearchKind.Script, "Script", Shape.Script, false),
    ];

    /// <summary>The controls of one tab (the ones it does not need are null).</summary>
    private sealed class QueryTab
    {
        public required SearchKind Kind { get; init; }
        public TextBox? Ids { get; set; }
        public TextBox? Pattern { get; set; }
        public CheckBox? Regex { get; set; }
        public CheckBox? IgnoreCase { get; set; }
        public ComboBox? Access { get; set; }
    }

    private readonly List<QueryTab> _queries = new();

    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ObservableCollection<SearchHit> _hits = new();
    private readonly ICollectionView _view;
    private Storage? _storage;
    private string _loadedPath = "";
    private CancellationTokenSource? _cancel;
    private bool _busy;

    public MainWindow()
    {
        InitializeComponent();
        _view = CollectionViewSource.GetDefaultView(_hits);
        _view.Filter = row => Matches((SearchHit)row);
        ResultsGrid.ItemsSource = _view;
        ResultsGrid.Loaded += (_, _) => MarkFilterButtons();

        BuildTabs();
        ApplySettings();
    }

    // ---- the settings ----

    private void ApplySettings()
    {
        Width = _settings.Width;
        Height = _settings.Height;
        if (!double.IsNaN(_settings.Left) && !double.IsNaN(_settings.Top) && IsOnScreen(_settings.Left, _settings.Top))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.Left;
            Top = _settings.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        if (_settings.Maximized)
            WindowState = WindowState.Maximized;

        _settings.RecentProjects = _settings.RecentProjects.Where(Directory.Exists).ToList();   // a project that was moved or deleted is not offered
        ProjectBox.ItemsSource = _settings.RecentProjects.ToList();
        ProjectBox.Text = _settings.ProjectPath;
        KindTabs.SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, kind => kind.Kind.ToString() == _settings.Kind));
        foreach (var query in _queries)
        {
            var state = _settings.Tabs.GetValueOrDefault(query.Kind.ToString()) ?? new TabState();
            query.Ids?.SetCurrentValue(TextBox.TextProperty, state.Ids);
            query.Pattern?.SetCurrentValue(TextBox.TextProperty, state.Pattern);
            query.Regex?.SetCurrentValue(ToggleButton.IsCheckedProperty, state.UseRegex);
            query.IgnoreCase?.SetCurrentValue(ToggleButton.IsCheckedProperty, state.IgnoreCase);
            query.Access?.SetCurrentValue(Selector.SelectedItemProperty, Enum.GetNames<AccessFilter>().Contains(state.Access) ? state.Access : nameof(AccessFilter.Both));
        }

        CeCheck.IsChecked = _settings.InCommonEvents;
        MapCheck.IsChecked = _settings.InMapEvents;
        TroopCheck.IsChecked = _settings.InTroops;
        FilterBox.Text = _settings.ResultFilter;
        ExpandCheck.IsChecked = _settings.ExpandCommonEvents;
        ExtendedCheck.IsChecked = _settings.ExtendedFormat;
        DumpFolderBox.Text = _settings.DumpFolder;
        foreach (var column in ResultsGrid.Columns)
        {
            if (_settings.ColumnWidths.TryGetValue(column.Header.ToString()!, out var width) && width > 10)
                column.Width = width;
        }
    }

    private static bool IsOnScreen(double left, double top) =>
        left > SystemParameters.VirtualScreenLeft - 50 && left < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
        top > SystemParameters.VirtualScreenTop - 10 && top < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100;

    private void CollectSettings()
    {
        var bounds = WindowState == WindowState.Maximized ? RestoreBounds : new Rect(Left, Top, Width, Height);
        _settings.Maximized = WindowState == WindowState.Maximized;
        _settings.Left = bounds.Left;
        _settings.Top = bounds.Top;
        _settings.Width = bounds.Width;
        _settings.Height = bounds.Height;
        _settings.ProjectPath = ProjectBox.Text.Trim();
        _settings.Kind = Current.Kind.ToString();
        foreach (var query in _queries)
        {
            _settings.Tabs[query.Kind.ToString()] = new TabState
            {
                Ids = query.Ids?.Text ?? "",
                Pattern = query.Pattern?.Text ?? "",
                UseRegex = query.Regex?.IsChecked == true,
                IgnoreCase = query.IgnoreCase?.IsChecked == true,
                Access = query.Access?.SelectedItem?.ToString() ?? nameof(AccessFilter.Both),
            };
        }

        _settings.InCommonEvents = CeCheck.IsChecked == true;
        _settings.InMapEvents = MapCheck.IsChecked == true;
        _settings.InTroops = TroopCheck.IsChecked == true;
        _settings.ResultFilter = FilterBox.Text;
        _settings.ExpandCommonEvents = ExpandCheck.IsChecked == true;
        _settings.ExtendedFormat = ExtendedCheck.IsChecked == true;
        _settings.DumpFolder = DumpFolderBox.Text.Trim();
        foreach (var column in ResultsGrid.Columns)
            _settings.ColumnWidths[column.Header.ToString()!] = column.ActualWidth;
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _cancel?.Cancel();
        CollectSettings();
        _settings.Save();
    }

    // ---- the project ----

    private async void Open_Click(object sender, RoutedEventArgs e) => await LoadProject();

    private async void ProjectBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await LoadProject();
    }

    private bool _updatingRecent;

    private async void ProjectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_updatingRecent && ProjectBox.SelectedItem is string path)
        {
            ProjectBox.Text = path;
            await LoadProject();
        }
    }

    // the project that was opened is the first of the list
    private void RememberProject(string path)
    {
        _settings.RecentProjects.RemoveAll(recent => string.Equals(recent, path, StringComparison.OrdinalIgnoreCase));
        _settings.RecentProjects.Insert(0, path);
        if (_settings.RecentProjects.Count > AppSettings.MaxRecentProjects)
            _settings.RecentProjects.RemoveRange(AppSettings.MaxRecentProjects, _settings.RecentProjects.Count - AppSettings.MaxRecentProjects);

        _updatingRecent = true;
        try
        {
            ProjectBox.ItemsSource = _settings.RecentProjects.ToList();
            ProjectBox.Text = path;
        }
        finally
        {
            _updatingRecent = false;
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "The folder of the project (MV: data or www; VX Ace: Data)", InitialDirectory = Directory.Exists(ProjectBox.Text) ? ProjectBox.Text : "" };
        if (dialog.ShowDialog(this) == true)
        {
            ProjectBox.Text = dialog.FolderName;
            _ = LoadProject();
        }
    }

    private async Task<bool> LoadProject()
    {
        var path = ProjectBox.Text.Trim();
        SetBusy(true, "Opening the project...");
        try
        {
            var (storage, description) = await Task.Run(() => ProjectLoader.Open(path));
            _storage = storage;
            _loadedPath = path;
            VersionText.Text = description;
            RememberProject(path);
            SetStatus("Project opened.");
            return true;
        }
        catch (Exception ex)
        {
            _storage = null;
            VersionText.Text = "";
            SetStatus("The project is not opened.");
            MessageBox.Show(this, ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- the search ----

    private QueryTab Current => _queries[Math.Max(0, KindTabs.SelectedIndex)];

    private void BuildTabs()
    {
        foreach (var (kind, title, shape, hasAccess) in Kinds)
        {
            var query = new QueryTab { Kind = kind };
            var panel = new WrapPanel { Margin = new Thickness(8, 8, 8, 2) };
            if (shape == Shape.Script)
            {
                var editor = new ScriptEditor();
                editor.SearchRequested += () =>
                {
                    if (!_busy)
                        Search_Click(editor.Box, new RoutedEventArgs());
                };
                query.Pattern = editor.Box;
                var field = Labeled("Expression: true for the commands to find", editor.Box);
                field.Children.Add(new TextBlock
                {
                    Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 4, 0, 0), MaxWidth = 820, TextWrapping = TextWrapping.Wrap,
                    Text = "Names: command (or c, NamedCommand), line, source (\"CE\", \"Map\", \"Troop\"), location. A type is checked with is: command is ShowPictureCommand p && p.Picture.StartsWith(\"great\"). "
                           + "A dot shows the members, Ctrl+Space the names, Ctrl+Enter searches. Empty finds every command.",
                });
                panel.Children.Add(field);
            }
            else if (shape == Shape.Text)
            {
                query.Pattern = new TextBox { Width = 380, Padding = new Thickness(2), ToolTip = kind == SearchKind.Pictures ? "The name of the picture; empty is any" : "The text of the command (its name and parameters); empty is any" };
                query.Regex = new CheckBox { Content = "Regular expression", ToolTip = "Off: the text is searched as it is" };
                query.IgnoreCase = new CheckBox { Content = "Ignore case" };
                panel.Children.Add(Labeled(kind == SearchKind.Pictures ? "Picture name" : "Text", query.Pattern));
                panel.Children.Add(Labeled("Options", new StackPanel { Orientation = Orientation.Horizontal, Height = 23, Children = { query.Regex, query.IgnoreCase } }));
            }
            else
            {
                query.Ids = new TextBox
                {
                    Width = 220, Padding = new Thickness(2),
                    ToolTip = shape == Shape.Letters ? "Empty is any. The letters: A, C" : "Empty is any. Ids and ranges: 1, 5-10, 40",
                };
                panel.Children.Add(Labeled(shape == Shape.Letters ? "Letters (A, C)" : kind == SearchKind.CommonEventCalls ? "Common event ids (1, 5-10)" : "Ids (1, 5-10)", query.Ids));
                if (shape == Shape.Ids)
                {
                    // the name as the editor shows it (the id and the name both have to fit)
                    query.Pattern = new TextBox { Width = 260, Padding = new Thickness(2), ToolTip = "Part of the name (or a regular expression); empty is any. The ids and the name both have to fit." };
                    query.Regex = new CheckBox { Content = "Regular expression", ToolTip = "Off: the text is searched as it is" };
                    query.IgnoreCase = new CheckBox { Content = "Ignore case" };
                    panel.Children.Add(Labeled("Name", query.Pattern));
                    panel.Children.Add(Labeled("Name options", new StackPanel { Orientation = Orientation.Horizontal, Height = 23, Children = { query.Regex, query.IgnoreCase } }));
                }
            }

            if (hasAccess)
            {
                query.Access = new ComboBox { Width = 110, ItemsSource = Enum.GetNames<AccessFilter>(), SelectedIndex = 0, ToolTip = "Set: the commands that change it. Check: the conditions and the reads." };
                panel.Children.Add(Labeled("Set / check", query.Access));
            }

            foreach (var box in new[] { query.Ids, query.Pattern })
            {
                // (Enter is a new line in the script)
                if (box != null && kind != SearchKind.Script)
                    box.KeyDown += Query_KeyDown;
            }

            _queries.Add(query);
            KindTabs.Items.Add(new TabItem { Header = title, Content = panel });
        }
    }

    private static StackPanel Labeled(string caption, UIElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 14, 6) };
        panel.Children.Add(new TextBlock { Text = caption, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(control);
        return panel;
    }

    private void Query_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_busy)
            Search_Click(sender, e);
    }

    private SearchRequest BuildRequest()
    {
        var query = Current;
        Regex? pattern = null;
        if (query.Kind != SearchKind.Script && query.Pattern is { Text.Length: > 0 })
        {
            var options = query.IgnoreCase?.IsChecked == true ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant;
            var text = query.Regex?.IsChecked == true ? query.Pattern.Text : Regex.Escape(query.Pattern.Text);
            pattern = new Regex(text, options, TimeSpan.FromSeconds(2));
        }

        return new SearchRequest
        {
            Kind = query.Kind,
            Ids = query.Ids == null ? IdFilter.Empty : IdFilter.Parse(query.Ids.Text, query.Kind == SearchKind.SelfSwitches),
            Pattern = pattern,
            ScriptText = query.Kind == SearchKind.Script ? query.Pattern?.Text ?? "" : "",
            Access = Enum.Parse<AccessFilter>(query.Access?.SelectedItem?.ToString() ?? nameof(AccessFilter.Both)),
            InCommonEvents = CeCheck.IsChecked == true,
            InMapEvents = MapCheck.IsChecked == true,
            InTroops = TroopCheck.IsChecked == true,
        };
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;

        SearchRequest request;
        try
        {
            request = BuildRequest();
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            MessageBox.Show(this, ex.Message, "Search", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!request.InCommonEvents && !request.InMapEvents && !request.InTroops)
        {
            MessageBox.Show(this, "Nothing to look in: check common events, map events or troops.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if ((_storage == null || _loadedPath != ProjectBox.Text.Trim()) && !await LoadProject())
            return;

        var storage = _storage!;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        SetBusy(true, "Searching...", cancellable: true);
        var started = DateTime.Now;
        try
        {
            var progress = new Progress<string>(SetStatus);
            var engine = new SearchEngine(storage, request);
            var hits = await Task.Run(() => engine.Run(progress, token), token);
            _columnFilters.Clear();   // the values of the new result are others
            _hits.Clear();
            foreach (var hit in hits)
                _hits.Add(hit);

            UpdateCount();
            SetStatus($"{hits.Count} hits on {hits.Select(hit => hit.Page.Key).Distinct().Count()} pages, {(DateTime.Now - started).TotalSeconds:0.0} s.");
            CollectSettings();
            _settings.Save();
            if (engine.ScriptErrors > 0)
            {
                SetStatus($"{hits.Count} hits; the script threw for {engine.ScriptErrors} commands.");
                MessageBox.Show(this, $"The script threw an exception for {engine.ScriptErrors} commands (they are not in the result). The first:{Environment.NewLine}{engine.FirstScriptError}", "Script", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (ScriptCompileException ex)
        {
            SetStatus("The script has errors.");
            MessageBox.Show(this, ex.Message, "Script", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (OperationCanceledException)
        {
            SetStatus("Cancelled.");
        }
        catch (RegexMatchTimeoutException)
        {
            SetStatus("The regular expression is too slow.");
            MessageBox.Show(this, "The regular expression takes too long on some text.", "Search", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            SetStatus("The search failed.");
            MessageBox.Show(this, ex.ToString(), "Search", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    // ---- the result ----

    // the text filter and the filters of the columns together (all of them have to fit)
    private bool Matches(SearchHit hit) => MatchesText(hit) && MatchesColumns(hit, except: null);

    private bool MatchesText(SearchHit hit)
    {
        var words = FilterBox.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
            return true;

        var line = string.Join(' ', hit.Source, hit.Location, hit.Trigger, hit.Conditions, hit.Command, hit.Details, hit.Matched, hit.Access);
        return words.All(word => line.Contains(word, StringComparison.OrdinalIgnoreCase));
    }

    private void FilterBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
        UpdateCount();
    }

    private void UpdateCount()
    {
        var shown = _view.Cast<object>().Count();
        CountText.Text = shown == _hits.Count ? $"{_hits.Count} rows" : $"{shown} of {_hits.Count} rows";
        ResultsGrid_SelectionChanged(this, null!);
        ClearFiltersButton.IsEnabled = FilterBox.Text.Length > 0 || _columnFilters.Count > 0;
        MarkFilterButtons();
    }

    private SearchHit? Selected => ResultsGrid.SelectedItem as SearchHit;

    private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        OpenPageButton.IsEnabled = Selected != null && !_busy;
        OpenEventButton.IsEnabled = Selected?.Page.Source == SourceKind.MapEvent && !_busy;
        OpenAllButton.IsEnabled = _view != null && !_busy && !_view.IsEmpty;
    }

    private async void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // the double click on the header or on the empty place is not a request for a dump
        if (e.OriginalSource is DependencyObject source && FindParent<DataGridRow>(source) != null)
            await OpenPages(false);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var current = child; current != null; current = System.Windows.Media.VisualTreeHelper.GetParent(current))
        {
            if (current is T found)
                return found;
        }

        return null;
    }

    private async void OpenPage_Click(object sender, RoutedEventArgs e) => await OpenPages(false);

    private async void OpenEvent_Click(object sender, RoutedEventArgs e) => await OpenPages(true);

    private void CopyLocation_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null)
            Clipboard.SetText(Selected.Location);
    }

    private void CopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (Selected != null)
            Clipboard.SetText(Selected.Details);
    }

    // ---- the filters of the columns ----

    private sealed class FilterItem : System.ComponentModel.INotifyPropertyChanged
    {
        private bool _isChecked;

        public required string Value { get; init; }
        public int Count { get; init; }
        public string Text => $"{(Value.Length == 0 ? "(empty)" : Value)}  ({Count})";

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value)
                    return;

                _isChecked = value;
                PropertyChanged?.Invoke(this, new(nameof(IsChecked)));
            }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    // the value of each column as the grid shows it (the key is the header of the column)
    private static readonly Dictionary<string, Func<SearchHit, string>> ColumnValues = new()
    {
        ["Source"] = hit => hit.Source,
        ["Location"] = hit => hit.Location,
        ["Trigger"] = hit => hit.Trigger,
        ["Page conditions"] = hit => hit.Conditions,
        ["Line"] = hit => hit.Line.ToString(),
        ["Command"] = hit => hit.Command,
        ["Details"] = hit => hit.Details,
        ["Matched"] = hit => hit.Matched,
        ["Access"] = hit => hit.Access,
        ["Hits on page"] = hit => hit.HitsOnPage.ToString(),
    };

    // more values than this are not listed (the search field of the popup finds the others)
    private const int MaxFilterValues = 5000;

    private readonly Dictionary<string, HashSet<string>> _columnFilters = new();
    private List<FilterItem> _filterItems = [];
    private string _filterColumn = "";
    private bool _filterTruncated;

    private bool MatchesColumns(SearchHit hit, string? except)
    {
        foreach (var (column, allowed) in _columnFilters)
        {
            if (column != except && !allowed.Contains(ColumnValues[column](hit)))
                return false;
        }

        return true;
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Button { Tag: string column } button || !ColumnValues.TryGetValue(column, out var valueOf))
            return;

        _filterColumn = column;
        // the values of the rows that the other filters (the text and the other columns) let in
        var groups = _hits.Where(hit => MatchesText(hit) && MatchesColumns(hit, except: column)).GroupBy(valueOf).ToList();
        var ordered = column is "Line" or "Hits on page"
            ? groups.OrderBy(group => int.Parse(group.Key))
            : groups.OrderBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase);
        var allowed = _columnFilters.GetValueOrDefault(column);
        _filterItems = ordered.Take(MaxFilterValues).Select(group => new FilterItem { Value = group.Key, Count = group.Count(), IsChecked = allowed?.Contains(group.Key) ?? true }).ToList();
        foreach (var item in _filterItems)
            item.PropertyChanged += (_, _) => UpdateAllBox();

        _filterTruncated = groups.Count > MaxFilterValues;
        FilterTitle.Text = column;
        FilterNote.Text = groups.Count > MaxFilterValues ? $"{groups.Count} values: the first {MaxFilterValues} are listed, search for the others." : "";
        FilterNote.Visibility = FilterNote.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FilterSearchBox.Text = "";
        ShowFilterItems();
        FilterPopup.PlacementTarget = button;
        FilterPopup.IsOpen = true;
        FilterSearchBox.Focus();
    }

    private IEnumerable<FilterItem> VisibleFilterItems => FilterSearchBox.Text.Length == 0
        ? _filterItems
        : _filterItems.Where(item => item.Value.Contains(FilterSearchBox.Text, StringComparison.OrdinalIgnoreCase));

    private void ShowFilterItems()
    {
        var visible = VisibleFilterItems.ToList();
        FilterList.ItemsSource = visible;
        FilterAllBox.Content = FilterSearchBox.Text.Length == 0 ? "(Select all)" : "(Select all search results)";
        UpdateAllBox();
    }

    private void UpdateAllBox()
    {
        var visible = VisibleFilterItems.ToList();
        var checkedCount = visible.Count(item => item.IsChecked);
        FilterAllBox.IsChecked = checkedCount == 0 ? false : checkedCount == visible.Count ? true : null;
    }

    private void FilterSearchBox_TextChanged(object sender, TextChangedEventArgs e) => ShowFilterItems();

    private void FilterAllBox_Click(object sender, RoutedEventArgs e)
    {
        // as in a table processor: with a search, only its results stay checked
        var visible = VisibleFilterItems.ToHashSet();
        var on = visible.Any(item => !item.IsChecked);   // some are not checked: check all; all are checked: clear
        foreach (var item in _filterItems)
            item.IsChecked = visible.Contains(item) ? on : (FilterSearchBox.Text.Length == 0 ? on : false);
    }

    private void FilterOk_Click(object sender, RoutedEventArgs e)
    {
        var checkedValues = _filterItems.Where(item => item.IsChecked).Select(item => item.Value).ToHashSet();
        // all the listed values are checked (and none is cut off): there is no filter
        if (checkedValues.Count == _filterItems.Count && !_filterTruncated)
            _columnFilters.Remove(_filterColumn);
        else
            _columnFilters[_filterColumn] = checkedValues;

        FilterPopup.IsOpen = false;
        ApplyFilters();
    }

    private void FilterClear_Click(object sender, RoutedEventArgs e)
    {
        _columnFilters.Remove(_filterColumn);
        FilterPopup.IsOpen = false;
        ApplyFilters();
    }

    private void FilterCancel_Click(object sender, RoutedEventArgs e) => FilterPopup.IsOpen = false;

    private void FilterPopup_Closed(object? sender, EventArgs e) => FilterList.ItemsSource = null;

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        _columnFilters.Clear();
        FilterBox.Text = "";   // refreshes the view
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _view.Refresh();
        UpdateCount();
    }

    // the button of a column that has a filter is blue
    private void MarkFilterButtons()
    {
        void Walk(DependencyObject parent)
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is Button { Tag: string column } button && ColumnValues.ContainsKey(column))
                {
                    var active = _columnFilters.ContainsKey(column);
                    button.Content = active ? "▼" : "▾";
                    button.Foreground = active ? System.Windows.Media.Brushes.DodgerBlue : SystemColors.ControlTextBrush;
                }

                Walk(child);
            }
        }

        Walk(ResultsGrid);
    }

    // ---- the dump ----

    private void DumpBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "The folder for the dumps", InitialDirectory = Directory.Exists(DumpFolderBox.Text) ? DumpFolderBox.Text : "" };
        if (dialog.ShowDialog(this) == true)
            DumpFolderBox.Text = dialog.FolderName;
    }

    private async Task OpenPages(bool wholeEvent)
    {
        if (Selected is not { } hit || _storage == null || _busy)
            return;

        var pages = wholeEvent && hit.Page.Source == SourceKind.MapEvent ? DumpService.PagesOfEvent(_storage, hit.Page).ToList() : [hit.Page];
        // the lines of the hits of the page are marked, the one of the chosen row first (the page is opened at it)
        var targets = pages.Select(page => new DumpTarget(page, LinesOf(_hits, page, page.Key == hit.Page.Key ? hit.Line : null))).ToList();
        await OpenDumps(targets, 6, count => $"The event has {count} pages: open all of them in the browser?");
    }

    private async void OpenAll_Click(object sender, RoutedEventArgs e)
    {
        // a dump is made for a page, so the rows of the same page are one dump; the rows that the filter hides are not opened
        var rows = _view.Cast<SearchHit>().ToList();
        var targets = rows.Select(hit => hit.Page).DistinctBy(page => page.Key).Select(page => new DumpTarget(page, LinesOf(rows, page, null))).ToList();
        await OpenDumps(targets, 10, count => $"The result has {count} different pages: write and open a dump for each of them in the browser?");
    }

    /// <summary>A page to dump and the lines of it to mark.</summary>
    private sealed record DumpTarget(PageRef Page, IReadOnlyList<int> Lines);

    // the lines of the hits on the page, in the order of the rows; a condition of the page has no line (-1)
    private static List<int> LinesOf(IEnumerable<SearchHit> hits, PageRef page, int? first)
    {
        var lines = hits.Where(hit => hit.Page.Key == page.Key && hit.Line >= 0).Select(hit => hit.Line).Distinct().ToList();
        if (first is >= 0 and var line)
        {
            lines.Remove(line);
            lines.Insert(0, line);
        }

        return lines;
    }

    /// <summary>Writes the dump of each page and opens it; asks first when there are more of them than the limit.</summary>
    private async Task OpenDumps(List<DumpTarget> pages, int askOver, Func<int, string> question)
    {
        if (_storage == null || _busy || pages.Count == 0)
            return;

        if (pages.Count > askOver && MessageBox.Show(this, question(pages.Count), "Open", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        CollectSettings();
        var storage = _storage;
        SetBusy(true, "Writing the dump...");
        try
        {
            var done = 0;
            foreach (var (page, lines) in pages)
            {
                var path = await Task.Run(() => DumpService.WriteAsync(storage, page, _settings, _loadedPath));
                DumpService.Open(path, lines);
                SetStatus($"Dumps opened: {++done} of {pages.Count}");
            }

            SetStatus(pages.Count == 1 ? "Dump opened." : $"{pages.Count} dumps opened.");
        }
        catch (Exception ex)
        {
            SetStatus("The dump failed.");
            MessageBox.Show(this, ex.ToString(), "Dump", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- the state of the window ----

    private void SetStatus(string text) => StatusText.Text = text;

    private void SetBusy(bool busy, string? text = null, bool cancellable = false)
    {
        _busy = busy;
        if (text != null)
            SetStatus(text);

        Progress.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
        Progress.IsIndeterminate = busy;
        SearchButton.IsEnabled = !busy;
        OpenButton.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy;
        CancelButton.IsEnabled = busy && cancellable;
        ResultsGrid_SelectionChanged(this, null!);
    }
}
