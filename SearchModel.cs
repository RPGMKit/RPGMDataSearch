namespace RPGMDataSearch;

public enum SearchKind
{
    CommonEventCalls,
    Pictures,
    Switches,
    Variables,
    SelfSwitches,
    Items,
    Weapons,
    Armors,
    Actors,
    TransferMap,
    CommandText,
}

public enum AccessFilter
{
    Both,
    Set,
    Check,
}

public enum SourceKind
{
    CommonEvent,
    MapEvent,
    Troop,
}

/// <summary>Which page a hit is on (what the dump is made of). PageIndex is from 0, as in the files of the dump.</summary>
public sealed record PageRef(SourceKind Source, int CommonEventId, int MapId, int EventId, int TroopId, int PageIndex)
{
    public string Key => $"{Source}/{CommonEventId}/{MapId}/{EventId}/{TroopId}/{PageIndex}";
}

public sealed class SearchRequest
{
    public SearchKind Kind { get; init; }
    public IdFilter Ids { get; init; } = IdFilter.Empty;
    public System.Text.RegularExpressions.Regex? Pattern { get; init; }
    public AccessFilter Access { get; init; }
    public bool InCommonEvents { get; init; } = true;
    public bool InMapEvents { get; init; } = true;
    public bool InTroops { get; init; } = true;
}

/// <summary>One command (or one condition of a page) that the search found; the properties are the columns of the grid.</summary>
public sealed class SearchHit
{
    public required PageRef Page { get; init; }
    public required string Source { get; init; }
    public required string Location { get; init; }
    public string Trigger { get; init; } = "";
    public string Conditions { get; init; } = "";

    /// <summary>The number of the command in the page (as in the dump); -1 is a condition of the page itself.</summary>
    public int Line { get; init; }
    public string Command { get; init; } = "";
    public string Details { get; init; } = "";
    public string Matched { get; init; } = "";
    public string Access { get; init; } = "";

    /// <summary>How many hits the same page has.</summary>
    public int HitsOnPage { get; set; }
}
