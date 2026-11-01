using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using RPGMDataKit.Data.Structures.Internal;

namespace RPGMDataSearch;

/// <summary>What the script of the tab "Script" sees.</summary>
public sealed class ScriptGlobals
{
    /// <summary>The command that is checked.</summary>
    public NamedCommand command = null!;

    /// <summary>The same command, shorter to type.</summary>
    public NamedCommand c => command;

    /// <summary>The number of the command in its page, from 0, as the dump numbers it.</summary>
    public int line;

    /// <summary>"CE", "Map" or "Troop".</summary>
    public string source = "";

    /// <summary>The place of the page, as the column Location shows it.</summary>
    public string location = "";
}

/// <summary>The script has errors; the message has them with the positions.</summary>
public sealed class ScriptCompileException(string message) : Exception(message);

/// <summary>Compiles the expression of the tab "Script" once into a function of a command that tells true or false.</summary>
internal static class ScriptCompiler
{
    // the namespaces and the types a script can name without a using
    public static readonly string[] Namespaces =
    [
        "System", "System.Linq", "System.Collections.Generic", "System.Text.RegularExpressions",
        "RPGMDataKit.Data.Enums", "RPGMDataKit.Data.Structures.Internal", "RPGMDataKit.Data.Structures.Commands",
        "RPGMDataKit.Data.Structures.Commands.IfCommands", "RPGMDataKit.Data.Structures.Commands.MovementCommands",
    ];

    private static readonly ScriptOptions Options = ScriptOptions.Default
        .WithReferences(typeof(NamedCommand).Assembly, typeof(Regex).Assembly, typeof(Enumerable).Assembly, typeof(List<>).Assembly)
        .WithImports(Namespaces);

    /// <summary>An empty script finds every command.</summary>
    public static ScriptRunner<bool> Compile(string code)
    {
        var script = CSharpScript.Create<bool>(string.IsNullOrWhiteSpace(code) ? "true" : code, Options, typeof(ScriptGlobals));
        var errors = script.Compile().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        if (errors.Count > 0)
        {
            throw new ScriptCompileException(string.Join(Environment.NewLine, errors.Select(error =>
            {
                var at = error.Location.GetLineSpan().StartLinePosition;
                return $"({at.Line + 1},{at.Character + 1}): {error.Id}: {error.GetMessage()}";
            })));
        }

        return script.CreateDelegate();
    }
}
