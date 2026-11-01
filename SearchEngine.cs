using RPGMDataKit;
using RPGMDataKit.Data.Enums;
using RPGMDataKit.Data.Structures.Commands;
using RPGMDataKit.Data.Structures.Commands.IfCommands;
using RPGMDataKit.Data.Structures.Interfaces;
using RPGMDataKit.Data.Structures.Internal;
using RPGMDataKit.Storages.Abstraction;

namespace RPGMDataSearch;

/// <summary>What a command (or a condition) refers to: an id or a range of ids, or a text (a picture, a self switch), and whether it sets it or only checks it.</summary>
internal readonly record struct Reference(SearchKind Kind, int From, int To, string Text, bool IsSet);

/// <summary>Walks the common events, the pages of the map events and of the troops, and finds what a request asks for.</summary>
internal sealed class SearchEngine
{
    private readonly Storage _storage;
    private readonly SearchRequest _request;
    private readonly EntityContext _context;
    private readonly DumpOptions _describeOptions = new();

    public SearchEngine(Storage storage, SearchRequest request)
    {
        _storage = storage;
        _request = request;
        _context = new EntityContext(storage, extendedFormat: true);
    }

    public List<SearchHit> Run(IProgress<string> progress, CancellationToken token)
    {
        var hits = new List<SearchHit>();
        if (_request.InCommonEvents)
        {
            progress.Report("Common events...");
            foreach (var (id, commonEvent) in _storage.CommonEvents.Get().OrderBy(pair => pair.Key))
            {
                token.ThrowIfCancellationRequested();
                if (commonEvent == null)
                    continue;

                var trigger = commonEvent.Trigger == CommonEventTriggerType.None ? "Call only" : commonEvent.Trigger.ToString();
                var conditions = commonEvent.Trigger == CommonEventTriggerType.None ? "" : "S" + commonEvent.SwitchId;
                var page = new PageRef(SourceKind.CommonEvent, id, 0, 0, 0, 0);
                if (commonEvent.Trigger != CommonEventTriggerType.None)
                    AddConditions(hits, page, "CE", $"CE{id} {commonEvent.Name}", trigger, conditions, [Switch(commonEvent.SwitchId)]);

                AddCommands(hits, page, "CE", $"CE{id} {commonEvent.Name}", trigger, conditions, commonEvent.NamedCommands);
            }
        }

        if (_request.InMapEvents)
        {
            var maps = _storage.MapInfos.Get().OrderBy(pair => pair.Key).ToList();
            var done = 0;
            foreach (var (mapId, mapInfo) in maps)
            {
                token.ThrowIfCancellationRequested();
                progress.Report($"Maps {++done}/{maps.Count}: {mapInfo?.Name}");
                if (_storage.Maps.Get(mapId) is not { } map)
                    continue;

                foreach (var (eventId, ev) in map.Events.OrderBy(pair => pair.Key))
                {
                    for (var i = 0; ev != null && i < ev.Pages.Count; i++)
                    {
                        if (ev.Pages[i] is not { } page)
                            continue;

                        var where = $"M{mapId} {mapInfo?.Name} › E{eventId} {ev.Name} ({ev.X},{ev.Y}) › P{i + 1}";
                        var pageRef = new PageRef(SourceKind.MapEvent, 0, mapId, eventId, 0, i);
                        var conditions = ConditionsText(page.Condition);
                        AddConditions(hits, pageRef, "Map", where, page.Trigger.ToString(), conditions, ConditionReferences(page.Condition));
                        AddCommands(hits, pageRef, "Map", where, page.Trigger.ToString(), conditions, page.NamedCommands);
                    }
                }
            }
        }

        if (_request.InTroops)
        {
            progress.Report("Troops...");
            foreach (var (id, troop) in _storage.Troops.Get().OrderBy(pair => pair.Key))
            {
                token.ThrowIfCancellationRequested();
                for (var i = 0; troop != null && i < troop.Pages.Count; i++)
                {
                    if (troop.Pages[i] is not { } page)
                        continue;

                    var where = $"T{id} {troop.Name} › P{i + 1}";
                    var pageRef = new PageRef(SourceKind.Troop, 0, 0, 0, id, i);
                    var conditions = page.Condition.SwitchValid ? "S" + page.Condition.SwitchId : "";
                    if (page.Condition.SwitchValid)
                        AddConditions(hits, pageRef, "Troop", where, "Battle", conditions, [Switch(page.Condition.SwitchId)]);

                    AddCommands(hits, pageRef, "Troop", where, "Battle", conditions, page.NamedCommands);
                }
            }
        }

        foreach (var group in hits.GroupBy(hit => hit.Page.Key))
        {
            var count = group.Count();
            foreach (var hit in group)
                hit.HitsOnPage = count;
        }

        return hits;
    }

    private void AddCommands(List<SearchHit> hits, PageRef page, string source, string where, string trigger, string conditions, IList<NamedCommand> commands)
    {
        for (var i = 0; i < commands.Count; i++)
        {
            var command = commands[i];
            if (_request.Kind == SearchKind.CommandText)
            {
                var text = command.GetCommandName(_storage.Version) + " " + Describe(command);
                if (_request.Pattern?.IsMatch(text) ?? true)
                    hits.Add(NewHit(page, source, where, trigger, conditions, i, command, "text", ""));

                continue;
            }

            foreach (var reference in References(command))
            {
                if (IsWanted(reference))
                    hits.Add(NewHit(page, source, where, trigger, conditions, i, command, Name(reference), reference.Kind is SearchKind.CommonEventCalls or SearchKind.Pictures or SearchKind.TransferMap ? "" : reference.IsSet ? "Set" : "Check"));
            }
        }
    }

    private void AddConditions(List<SearchHit> hits, PageRef page, string source, string where, string trigger, string conditions, IEnumerable<Reference> references)
    {
        foreach (var reference in references)
        {
            if (IsWanted(reference))
                hits.Add(NewHit(page, source, where, trigger, conditions, -1, null, Name(reference), "Check"));
        }
    }

    private SearchHit NewHit(PageRef page, string source, string where, string trigger, string conditions, int line, NamedCommand? command, string matched, string access) => new()
    {
        Page = page, Source = source, Location = where, Trigger = trigger, Conditions = conditions, Line = line, Matched = matched, Access = access,
        Command = command?.GetCommandName(_storage.Version) ?? "(page condition)",
        Details = command == null ? conditions : Describe(command),
    };

    private string Describe(NamedCommand command)
    {
        try
        {
            return command.GetParametersDescription(_context, _describeOptions).ReplaceLineEndings(" ");
        }
        catch
        {
            return "";   // a description that cannot be made (a command that needs the context of a page) is not a reason to lose the hit
        }
    }

    private bool IsWanted(Reference reference)
    {
        if (reference.Kind != _request.Kind)
            return false;

        if (_request.Kind is SearchKind.Switches or SearchKind.Variables or SearchKind.SelfSwitches or SearchKind.Items or SearchKind.Weapons or SearchKind.Armors or SearchKind.Actors)
        {
            if (_request.Access == AccessFilter.Set && !reference.IsSet || _request.Access == AccessFilter.Check && reference.IsSet)
                return false;
        }

        return _request.Kind switch
        {
            SearchKind.Pictures => _request.Pattern?.IsMatch(reference.Text) ?? true,
            SearchKind.SelfSwitches => _request.Ids.Matches(reference.Text),
            // the ids and the name both have to fit (the one that is empty fits everything)
            _ => _request.Ids.Matches(reference.From, reference.To) && NameFits(reference),
        };
    }

    // the name of the thing (of any id of a range) is matched by the pattern of the request
    private bool NameFits(Reference reference)
    {
        if (_request.Pattern == null)
            return true;

        for (var id = reference.From; id <= reference.To && id < reference.From + MaxRangeToName; id++)
        {
            if (NameOf(reference.Kind, id) is { Length: > 0 } name && _request.Pattern.IsMatch(name))
                return true;
        }

        return false;
    }

    private const int MaxRangeToName = 5000;

    /// <summary>The name that the editor shows: of a common event, a switch, a variable, an item, a weapon, an armor, an actor.</summary>
    private string? NameOf(SearchKind kind, int id)
    {
        try
        {
            return kind switch
            {
                SearchKind.CommonEventCalls => _storage.CommonEvents.Get(id)?.Name,
                SearchKind.Switches => _storage.GetSwitch(id),
                SearchKind.Variables => _storage.GetVariable(id),
                SearchKind.Items => _storage.Items.Get(id)?.Name,
                SearchKind.Weapons => _storage.Weapons.Get(id)?.Name,
                SearchKind.Armors => _storage.Armors.Get(id)?.Name,
                SearchKind.Actors => _storage.Actors.Get(id)?.Name,
                SearchKind.TransferMap => _storage.MapInfos.Get(id)?.Name,
                _ => null,
            };
        }
        catch
        {
            return null;   // no such id in the database: it has no name
        }
    }

    // ---- what a command refers to ----

    private static Reference Switch(int id) => new(SearchKind.Switches, id, id, "", false);

    private static IEnumerable<Reference> ConditionReferences(IEventPageCondition condition)
    {
        if (condition.Switch1Valid)
            yield return Switch(condition.Switch1Id);
        if (condition.Switch2Valid)
            yield return Switch(condition.Switch2Id);
        if (condition.VariableValid)
            yield return new(SearchKind.Variables, condition.VariableId, condition.VariableId, "", false);
        if (condition.SelfSwitchValid)
            yield return new(SearchKind.SelfSwitches, 0, 0, condition.SelfSwitchCh, false);
        if (condition.ItemValid)
            yield return new(SearchKind.Items, condition.ItemId, condition.ItemId, "", false);
        if (condition.ActorValid)
            yield return new(SearchKind.Actors, condition.ActorId, condition.ActorId, "", false);
    }

    private static string ConditionsText(IEventPageCondition condition)
    {
        var parts = new List<string>();
        if (condition.Switch1Valid)
            parts.Add("S" + condition.Switch1Id);
        if (condition.Switch2Valid)
            parts.Add("S" + condition.Switch2Id);
        if (condition.VariableValid)
            parts.Add($"V{condition.VariableId} >= {condition.VariableValue}");
        if (condition.SelfSwitchValid)
            parts.Add("Self " + condition.SelfSwitchCh);
        if (condition.ItemValid)
            parts.Add("Item " + condition.ItemId);
        if (condition.ActorValid)
            parts.Add("Actor " + condition.ActorId);
        return string.Join(", ", parts);
    }

    private static Reference Id(SearchKind kind, int id, bool isSet) => new(kind, id, id, "", isSet);

    private static bool TryInt(object? value, out int result)
    {
        try
        {
            result = Convert.ToInt32(value);
            return true;
        }
        catch
        {
            result = 0;
            return false;
        }
    }

    private static IEnumerable<Reference> References(NamedCommand command)
    {
        switch (command)
        {
            case CallCommonEventCommand call:
                yield return Id(SearchKind.CommonEventCalls, call.EventId, false);
                break;
            case ShowPictureCommand picture:
                yield return new(SearchKind.Pictures, 0, 0, picture.Picture ?? "", false);
                break;
            case ControlSwitchCommand switches:
                yield return new(SearchKind.Switches, switches.StartSwitchId, switches.EndSwitchId, "", true);
                break;
            case ControlVariableCommand variables:
                yield return new(SearchKind.Variables, variables.StartVariable, variables.EndVariable, "", true);
                // the value is read from another variable
                if (variables.Operand == ControlVariableCommand.OperandType.Variable && TryInt(variables.Value1, out var source))
                    yield return Id(SearchKind.Variables, source, false);
                // the value is read from the game: Value1 is what (the number of an item...), Value2 is its id (the layout of GetParametersDescription of the command)
                if (variables.Operand == ControlVariableCommand.OperandType.GameData && TryInt(variables.Value1, out var type) && TryInt(variables.Value2, out var dataId))
                {
                    var kind = (ControlVariableCommand.GameDataType)type switch
                    {
                        ControlVariableCommand.GameDataType.ItemInInventory => SearchKind.Items,
                        ControlVariableCommand.GameDataType.WeaponInInventory => SearchKind.Weapons,
                        ControlVariableCommand.GameDataType.ArmorInInventory => SearchKind.Armors,
                        ControlVariableCommand.GameDataType.Actor => SearchKind.Actors,
                        _ => (SearchKind?)null,
                    };
                    if (kind != null)
                        yield return Id(kind.Value, dataId, false);
                }

                break;
            case ControlSelfSwitchCommand selfSwitch:
                yield return new(SearchKind.SelfSwitches, 0, 0, selfSwitch.Switch ?? "", true);
                break;
            case IfSwitchCommand ifSwitch:
                yield return Id(SearchKind.Switches, ifSwitch.SwitchId, false);
                break;
            case IfVariableCommand ifVariable:
                yield return Id(SearchKind.Variables, ifVariable.VariableId, false);
                if (ifVariable.Operand == IfVariableCommand.OperandType.Variable)
                    yield return Id(SearchKind.Variables, ifVariable.Value, false);
                break;
            case IfSelfSwitchCommand ifSelf:
                yield return new(SearchKind.SelfSwitches, 0, 0, ifSelf.Switch ?? "", false);
                break;
            case IfItemCommand ifItem:
                yield return Id(SearchKind.Items, ifItem.ItemId, false);
                break;
            case IfWeaponCommand ifWeapon:
                yield return Id(SearchKind.Weapons, ifWeapon.WeaponId, false);
                break;
            case IfArmorCommand ifArmor:
                yield return Id(SearchKind.Armors, ifArmor.ArmorId, false);
                break;
            case IfActorCommand ifActor:
                yield return Id(SearchKind.Actors, ifActor.ActorId, false);
                break;
            case ChangeItemsCommand items:
                yield return Id(SearchKind.Items, items.ItemId, true);
                if (items.Operand == ChangeItemsCommand.OperandType.Variable)
                    yield return Id(SearchKind.Variables, items.Value, false);
                break;
            case ChangeWeaponsCommand weapons:
                yield return Id(SearchKind.Weapons, weapons.WeaponId, true);
                if (weapons.Operand == ChangeWeaponsCommand.OperandType.Variable)
                    yield return Id(SearchKind.Variables, weapons.Value, false);
                break;
            case ChangeArmorsCommand armors:
                yield return Id(SearchKind.Armors, armors.ArmorId, true);
                if (armors.Operand == ChangeArmorsCommand.OperandType.Variable)
                    yield return Id(SearchKind.Variables, armors.Value, false);
                break;
            case TransferPlayerCommand transfer:
                // a transfer by variables has no map to tell (the variables hold it when the game runs)
                yield return transfer.Designation == TransferPlayerCommand.DesignationType.Direct
                    ? Id(SearchKind.TransferMap, transfer.Param1, false)
                    : new(SearchKind.TransferMap, 0, 0, "by variables", false);
                break;
            case ChangePartyMemberCommand member:
                yield return Id(SearchKind.Actors, member.ActorId, true);
                break;
        }
    }

    // ---- the names for the column "Matched" ----

    private string Name(Reference reference)
    {
        string Span(string label, Func<int, string?> nameOf) => reference.From == reference.To
            ? $"{label} {reference.From} {nameOf(reference.From)}".TrimEnd()
            : $"{label}s {reference.From}..{reference.To}";

        try
        {
            return reference.Kind switch
            {
                SearchKind.CommonEventCalls => Span("CE", id => NameOf(reference.Kind, id)),
                SearchKind.Pictures => reference.Text,
                SearchKind.SelfSwitches => "Self switch " + reference.Text,
                SearchKind.Switches => Span("Switch", id => NameOf(reference.Kind, id)),
                SearchKind.Variables => Span("Variable", id => NameOf(reference.Kind, id)),
                SearchKind.Items => Span("Item", id => NameOf(reference.Kind, id)),
                SearchKind.Weapons => Span("Weapon", id => NameOf(reference.Kind, id)),
                SearchKind.Armors => Span("Armor", id => NameOf(reference.Kind, id)),
                SearchKind.Actors => Span("Actor", id => NameOf(reference.Kind, id)),
                SearchKind.TransferMap => reference.Text.Length > 0 ? "Transfer " + reference.Text : Span("Map", id => NameOf(reference.Kind, id)),
                _ => "",
            };
        }
        catch
        {
            return reference.From.ToString();
        }
    }
}
