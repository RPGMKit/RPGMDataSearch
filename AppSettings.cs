using System.Text.Json;

namespace RPGMDataSearch;

/// <summary>Every setting of the program; it is kept in settings.json next to the executable.</summary>
public sealed class TabState
{
    public string Ids { get; set; } = "";
    public string Pattern { get; set; } = "";
    public bool UseRegex { get; set; } = true;
    public bool IgnoreCase { get; set; } = true;
    public string Access { get; set; } = nameof(AccessFilter.Both);
}

public sealed class AppSettings
{
    public string ProjectPath { get; set; } = "";

    /// <summary>The projects that were opened, the last one first.</summary>
    public List<string> RecentProjects { get; set; } = new();

    public const int MaxRecentProjects = 10;
    /// <summary>The tab of the search that was open (a name of SearchKind).</summary>
    public string Kind { get; set; } = nameof(SearchKind.CommonEventCalls);

    /// <summary>What was typed on each tab, by the name of SearchKind.</summary>
    public Dictionary<string, TabState> Tabs { get; set; } = new();
    public bool InCommonEvents { get; set; } = true;
    public bool InMapEvents { get; set; } = true;
    public bool InTroops { get; set; } = true;
    public string ResultFilter { get; set; } = "";

    public bool ExpandCommonEvents { get; set; } = true;
    public bool ExtendedFormat { get; set; } = true;
    /// <summary>Where the dumps go: a folder for each project (DumpService.FolderOf) is made in it.</summary>
    public string DumpFolder { get; set; } = DefaultDumpFolder;

    public static string DefaultDumpFolder => Path.Combine(AppContext.BaseDirectory, "temp");

    public double Left { get; set; } = double.NaN;
    public double Top { get; set; } = double.NaN;
    public double Width { get; set; } = 1250;
    public double Height { get; set; } = 720;
    public bool Maximized { get; set; }
    public Dictionary<string, double> ColumnWidths { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var settings = File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
            if (string.IsNullOrWhiteSpace(settings.DumpFolder) || settings.DumpFolder == Path.Combine(Path.GetTempPath(), "RPGMDataSearch"))
                settings.DumpFolder = DefaultDumpFolder;   // the earlier default, which was in the system temp

            return settings;
        }
        catch
        {
            return new();   // a broken file is not a reason not to start
        }
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch
        {
            // the folder of the program can be read-only; the settings are lost then, the program works
        }
    }
}
