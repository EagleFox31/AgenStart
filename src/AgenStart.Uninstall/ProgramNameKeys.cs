using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

/// <summary>
/// Comparison keys for strict leftover matching (design §5): the folded name without its trailing
/// version, and for publishers without a legal suffix. Two names match only when their keys are
/// equal; there is no substring or pattern matching.
/// </summary>
public static partial class ProgramNameKeys
{
    public static string Product(string? name)
    {
        var key = ProgramNameNormalizer.Normalize(name);
        string previous;
        do
        {
            previous = key;
            key = TrailingVersion().Replace(key, string.Empty).Trim();
        }
        while (key != previous && key.Length > 0);

        return key;
    }

    public static string Publisher(string? name)
    {
        var key = Product(name);
        string previous;
        do
        {
            previous = key;
            key = LegalSuffix().Replace(key, string.Empty).Trim().TrimEnd(',', '.').Trim();
        }
        while (key != previous && key.Length > 0);

        return key;
    }

    [GeneratedRegex(@"(^|\s+)v?\d+(\.\d+)*[a-z]?$")]
    private static partial Regex TrailingVersion();

    [GeneratedRegex(@"[\s,]+(inc|ltd|llc|gmbh|corporation|corp|co|s\.a|sa|sas|sarl|limited|ag|bv|srl|pty)\.?$")]
    private static partial Regex LegalSuffix();
}
