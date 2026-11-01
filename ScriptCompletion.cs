using System.Reflection;
using System.Text.RegularExpressions;
using RPGMDataKit.Data.Structures.Internal;

namespace RPGMDataSearch;

/// <summary>A line of the list of the suggestions: the name to insert and what it is.</summary>
internal sealed record CompletionItem(string Name, string Detail);

/// <summary>
/// The suggestions of the editor of the script, by reflection: what follows a dot (<c>command.</c>, <c>p.Picture.</c>) is looked up from the names written before it.
/// The names that are known: the variables of the script, the variables of the patterns in the text (<c>is ShowPictureCommand p</c>) and the types of the library.
/// A call or an indexer in the chain is not followed.
/// </summary>
internal static class ScriptCompletion
{
    private static readonly Dictionary<string, Type> Globals = new()
    {
        ["command"] = typeof(NamedCommand),
        ["c"] = typeof(NamedCommand),
        ["line"] = typeof(int),
        ["source"] = typeof(string),
        ["location"] = typeof(string),
    };

    // the names of the types that C# has keywords for (is string p)
    private static readonly Dictionary<string, Type> Keywords = new()
    {
        ["string"] = typeof(string), ["int"] = typeof(int), ["uint"] = typeof(uint), ["long"] = typeof(long), ["ulong"] = typeof(ulong), ["short"] = typeof(short),
        ["ushort"] = typeof(ushort), ["byte"] = typeof(byte), ["sbyte"] = typeof(sbyte), ["bool"] = typeof(bool), ["char"] = typeof(char), ["float"] = typeof(float),
        ["double"] = typeof(double), ["decimal"] = typeof(decimal), ["object"] = typeof(object),
    };

    private static readonly Lazy<Dictionary<string, Type>> Types = new(() =>
    {
        var types = new Dictionary<string, Type>();
        var wanted = ScriptCompiler.Namespaces.ToHashSet();
        foreach (var type in typeof(NamedCommand).Assembly.GetExportedTypes().Concat([typeof(Regex), typeof(Enumerable), typeof(Math), typeof(string)]))
        {
            if (type.Namespace != null && wanted.Contains(type.Namespace) && !type.IsNested)
                types.TryAdd(type.Name, type);
        }

        return types;
    });

    // "is ShowPictureCommand p", "is not X x" does not bind: a pattern variable and its type
    private static readonly Regex PatternVariable = new(@"\bis\s+(?<type>[A-Za-z_]\w*)\s+(?<name>[A-Za-z_]\w*)", RegexOptions.Compiled);

    private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private static bool IsIdentifier(char c) => char.IsLetterOrDigit(c) || c == '_';

    /// <summary>
    /// The suggestions for the place of the caret and the position where the word that is typed begins (it is replaced by the chosen name); null when there is none.
    /// After a dot: the members of what is before it. Without one, only when forced: the names of the script and the types.
    /// </summary>
    public static (int Start, List<CompletionItem> Items)? Complete(string text, int caret, bool force)
    {
        try
        {
            return Find(text, caret, force);
        }
        catch (Exception)
        {
            return null;   // no suggestions are better than a fall of the editor
        }
    }

    private static (int Start, List<CompletionItem> Items)? Find(string text, int caret, bool force)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        var start = caret;
        while (start > 0 && IsIdentifier(text[start - 1]))
            start--;

        var partial = text[start..caret];
        var items = start > 0 && text[start - 1] == '.' ? MembersAfterDot(text, start - 1) : force ? Names(text) : null;
        if (items == null)
            return null;

        var found = items.Where(item => item.Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase)).OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return found.Count == 0 ? null : (start, found);
    }

    private static List<CompletionItem> Names(string text)
    {
        var variables = PatternVariables(text);
        var items = Globals.Where(pair => !variables.ContainsKey(pair.Key)).Select(pair => new CompletionItem(pair.Key, TypeName(pair.Value))).ToList();
        items.AddRange(variables.Select(pair => new CompletionItem(pair.Key, TypeName(pair.Value))));
        items.AddRange(Types.Value.Select(pair => new CompletionItem(pair.Key, pair.Value.IsEnum ? "enum" : "type")));
        items.AddRange(new[] { "true", "false", "null" }.Select(word => new CompletionItem(word, "")));
        return items;
    }

    private static Dictionary<string, Type> PatternVariables(string text)
    {
        var variables = new Dictionary<string, Type>();
        foreach (Match match in PatternVariable.Matches(text))
        {
            var name = match.Groups["type"].Value;
            if (Keywords.TryGetValue(name, out var type) || Types.Value.TryGetValue(name, out type))
                variables[match.Groups["name"].Value] = type;
        }

        return variables;
    }

    // the chain of names before the dot (a.b.c) is followed from its first name to the last, and the members of the last one are the suggestions
    private static List<CompletionItem>? MembersAfterDot(string text, int dot)
    {
        var chain = new List<string>();
        var at = dot;
        while (true)
        {
            var end = at;
            if (end > 0 && text[end - 1] == '?')
                end--;   // a?.b

            var begin = end;
            while (begin > 0 && IsIdentifier(text[begin - 1]))
                begin--;

            if (begin == end)
                return null;   // a call, an indexer, a literal: not followed

            chain.Insert(0, text[begin..end]);
            if (begin > 0 && text[begin - 1] == '.')
                at = begin - 1;
            else
                break;
        }

        var variables = PatternVariables(text);
        Type? type;
        var isStatic = false;
        if (variables.TryGetValue(chain[0], out type) || Globals.TryGetValue(chain[0], out type))
        {
        }
        else if (Types.Value.TryGetValue(chain[0], out type))
            isStatic = true;
        else
            return null;

        foreach (var name in chain.Skip(1))
        {
            var member = type.GetMembers(Members).FirstOrDefault(m => m.Name == name && (isStatic ? IsStatic(m) : !IsStatic(m)) && m.MemberType is MemberTypes.Property or MemberTypes.Field or MemberTypes.NestedType);
            if (member == null && type.IsInterface)
                member = type.GetInterfaces().SelectMany(i => i.GetMembers(Members)).FirstOrDefault(m => m.Name == name && m.MemberType is MemberTypes.Property or MemberTypes.Field);

            switch (member)
            {
                case PropertyInfo property:
                    type = property.PropertyType;
                    isStatic = false;
                    break;
                case FieldInfo field:
                    type = field.FieldType;
                    isStatic = false;
                    break;
                case Type nested:
                    type = nested;
                    isStatic = true;
                    break;
                default:
                    return null;
            }

            type = Nullable.GetUnderlyingType(type) ?? type;
        }

        return MembersOf(type, isStatic);
    }

    private static bool IsStatic(MemberInfo member) => member switch
    {
        PropertyInfo property => property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false,
        FieldInfo field => field.IsStatic,
        MethodInfo method => method.IsStatic,
        Type => true,
        _ => false,
    };

    private static List<CompletionItem> MembersOf(Type type, bool isStatic)
    {
        // the values of an enum are all that follows its name (not the methods of Enum)
        if (type.IsEnum && isStatic)
            return Enum.GetNames(type).Select(name => new CompletionItem(name, "")).ToList();

        // an interface lists only its own members: the ones it extends are added
        var types = type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()) : [type];
        var items = new Dictionary<string, CompletionItem>();
        foreach (var member in types.SelectMany(t => t.GetMembers(Members)))
        {
            if (member.DeclaringType == typeof(object) && member.Name != nameof(ToString))
                continue;

            // (the types inside a type follow its name only; an indexer is not a name)
            if (member is MethodBase { IsSpecialName: true } || member is ConstructorInfo || (member is Type ? !isStatic : IsStatic(member) != isStatic) || member is PropertyInfo { } indexer && indexer.GetIndexParameters().Length > 0)
                continue;

            var detail = member switch
            {
                PropertyInfo property => TypeName(property.PropertyType),
                FieldInfo field => field.IsLiteral && field.DeclaringType!.IsEnum ? "" : TypeName(field.FieldType),
                MethodInfo method => "(" + string.Join(", ", method.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name)) + ") : " + TypeName(method.ReturnType),
                Type nested => "type",
                _ => null,
            };
            if (detail != null)
                items.TryAdd(member.Name, new CompletionItem(member.Name, detail));
        }

        return [.. items.Values];
    }

    private static string TypeName(Type type)
    {
        if (type.IsByRef)
            return TypeName(type.GetElementType()!);

        if (type == typeof(int)) return "int";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(void)) return "void";
        if (type == typeof(object)) return "object";
        if (Nullable.GetUnderlyingType(type) is { } inner) return TypeName(inner) + "?";
        if (type.IsArray) return TypeName(type.GetElementType()!) + "[]";
        // (a type inside a generic type, such as List<T>.Enumerator, is generic without having a mark of it in its name)
        if (type.IsGenericType && type.Name.IndexOf('`') is var mark and >= 0)
            return type.Name[..mark] + "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";

        return type.Name;
    }
}
