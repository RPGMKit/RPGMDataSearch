using RPGMDataKit;
using RPGMDataKit.Data.Helpers;
using RPGMDataKit.Data.Structures.Internal;
using RPGMDataKit.Storages.Abstraction;

namespace RPGMDataSearch;

/// <summary>Writes the html dump of a page (a common event, a page of a map event or of a troop) with the library, as DumpHelper does for the whole project.</summary>
internal static class DumpService
{
    /// <summary>The folder of the dumps of a project: the folder of the settings and the hash of the path of the project, so the projects do not mix.</summary>
    public static string FolderOf(string dumpFolder, string projectPath)
    {
        var normalized = Path.GetFullPath(projectPath.Trim().Trim('"')).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(normalized)), 0, 8);
        return Path.Combine(dumpFolder, hash);
    }

    public static async Task<string> WriteAsync(Storage storage, PageRef page, AppSettings settings, string projectPath)
    {
        var options = new DumpOptions
        {
            Format = DumpFormat.Html,
            ExtendedFormat = settings.ExtendedFormat,
            ExpandCommonEvents = settings.ExpandCommonEvents,
        };

        var folder = FolderOf(settings.DumpFolder, projectPath);
        switch (page.Source)
        {
            case SourceKind.CommonEvent:
            {
                var commonEvent = storage.CommonEvents.Get(page.CommonEventId) ?? throw new InvalidOperationException($"There is no common event {page.CommonEventId}");
                IndentHelper.ValidateIndents(commonEvent, storage.Version);
                var context = new EntityContext(storage, new CommonEventContext { CommonEvent = commonEvent }, options.ExtendedFormat);
                return await Write(Path.Combine(folder, "CE", $"CE{page.CommonEventId:D3}.html"), writer => commonEvent.Dump(writer, context, options));
            }
            case SourceKind.MapEvent:
            {
                var map = storage.Maps.Get(page.MapId) ?? throw new InvalidOperationException($"There is no map {page.MapId}");
                var ev = map.Events[page.EventId] ?? throw new InvalidOperationException($"There is no event {page.EventId} on the map {page.MapId}");
                var eventPage = ev.Pages[page.PageIndex] ?? throw new InvalidOperationException("There is no such page");
                IndentHelper.ValidateIndents(eventPage, storage.Version);
                var context = new EntityContext(storage, new MapEventContext { MapId = page.MapId, Map = map, Event = ev, PageId = page.PageIndex, EventPage = eventPage }, options.ExtendedFormat);
                return await Write(Path.Combine(folder, "ME", $"M{page.MapId:D3}_E{page.EventId:D3}_P{page.PageIndex}.html"), writer => eventPage.Dump(writer, context, options));
            }
            default:
            {
                var troop = storage.Troops.Get(page.TroopId) ?? throw new InvalidOperationException($"There is no troop {page.TroopId}");
                var troopPage = troop.Pages[page.PageIndex] ?? throw new InvalidOperationException("There is no such page");
                IndentHelper.ValidateIndents(troopPage, storage.Version);
                var context = new EntityContext(storage, new TroopContext { Troop = troop, PageId = page.PageIndex, EventPage = troopPage }, options.ExtendedFormat);
                return await Write(Path.Combine(folder, "TE", $"T{page.TroopId:D3}_P{page.PageIndex}.html"), writer => troopPage.Dump(writer, context, options));
            }
        }
    }

    /// <summary>
    /// Opens the dump in the browser. With lines, the page marks them and scrolls to the first (the address <c>page.html#lines=12,40</c> of the dump). The shell takes a path to open,
    /// and a # in it is a part of the name of the file, so a little page next to the dump leads to the address.
    /// </summary>
    public static void Open(string dumpPath, IReadOnlyList<int> lines)
    {
        var marked = lines.Where(line => line >= 0).Distinct().ToList();
        var target = dumpPath;
        if (marked.Count > 0)
        {
            target = dumpPath[..^Path.GetExtension(dumpPath).Length] + ".marked.html";
            var address = Uri.EscapeDataString(Path.GetFileName(dumpPath)) + "#lines=" + string.Join(',', marked);
            File.WriteAllText(target, "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><title>Open the dump</title></head><body>"
                                      + $"<script>location.replace({System.Text.Json.JsonSerializer.Serialize(address)});</script>"
                                      + $"<noscript><a href=\"{System.Net.WebUtility.HtmlEncode(address)}\">Open the dump</a></noscript></body></html>");
        }

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true });
    }

    /// <summary>Every page of the map event of the page.</summary>
    public static IEnumerable<PageRef> PagesOfEvent(Storage storage, PageRef page)
    {
        var count = storage.Maps.Get(page.MapId)?.Events[page.EventId]?.Pages.Count ?? 0;
        for (var i = 0; i < count; i++)
            yield return page with { PageIndex = i };
    }

    private static async Task<string> Write(string path, Func<TextWriter, Task> dump)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Open(path, FileMode.Create);
        await using var writer = new StreamWriter(file);
        await dump(writer);
        return path;
    }
}
