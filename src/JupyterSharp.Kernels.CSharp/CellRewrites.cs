using System.Text.RegularExpressions;

namespace JupyterSharp.Kernels.CSharp;

/// <summary>Source rewrites that make code written for .NET Interactive / Polyglot Notebooks compile under Roslyn scripting.</summary>
public static class CellRewrites
{
    private static readonly Regex TopLevelUsingVar = new(@"^using[ \t]+var[ \t]+", RegexOptions.Compiled | RegexOptions.Multiline | RegexOptions.CultureInvariant);

    /// <summary>
    /// Roslyn scripting rejects a top-level <c>using var x = ...;</c> (CS1002). It becomes a plain <c>var x = ...;</c>, so the object is NOT
    /// disposed at the end of the cell; it lives as long as the session. Only declarations that start in column 0 are changed: indented ones
    /// are inside a method or block, where <c>using var</c> is valid and keeps its disposal.
    /// </summary>
    public static string ForScripting(string code) => TopLevelUsingVar.Replace(code, "var ");
}
