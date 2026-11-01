namespace RPGMDataSearch;

/// <summary>"1, 5-10, 40" for the ids; the letters of self switches ("A, C") for those; empty is everything.</summary>
public sealed class IdFilter
{
    public static readonly IdFilter Empty = new([], []);

    private readonly List<(int From, int To)> _ranges;
    private readonly HashSet<string> _letters;

    private IdFilter(List<(int, int)> ranges, HashSet<string> letters)
    {
        _ranges = ranges;
        _letters = letters;
    }

    public bool IsEmpty => _ranges.Count == 0 && _letters.Count == 0;

    public static IdFilter Parse(string text, bool letters)
    {
        var parts = text.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (letters)
            return new([], new(parts.Select(part => part.ToUpperInvariant())));

        var ranges = new List<(int, int)>();
        foreach (var part in parts)
        {
            var bounds = part.Split('-', 2, StringSplitOptions.TrimEntries);
            if (!int.TryParse(bounds[0], out var from) || (bounds.Length == 2 && !int.TryParse(bounds[1], out _)))
                throw new FormatException($"\"{part}\" is not an id or a range of ids (1, 5-10)");

            var to = bounds.Length == 2 ? int.Parse(bounds[1]) : from;
            ranges.Add((Math.Min(from, to), Math.Max(from, to)));
        }

        return new(ranges, []);
    }

    /// <summary>A command works on the ids From..To (a range of switches or variables is one command): any of them is enough.</summary>
    public bool Matches(int from, int to) => IsEmpty || _ranges.Any(range => from <= range.To && to >= range.From);

    public bool Matches(string letter) => IsEmpty || _letters.Contains(letter.ToUpperInvariant());
}
