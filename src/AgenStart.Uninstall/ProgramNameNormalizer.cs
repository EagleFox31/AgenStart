using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AgenStart.Uninstall;

/// <summary>
/// Folds a program name for comparison and search: case, accents, trademark signs, repeated
/// whitespace and a trailing parenthesised architecture tag such as <c>(x64)</c> or <c>(64-bit)</c>.
/// Versions in the name are kept: they distinguish side-by-side installations.
/// </summary>
public static partial class ProgramNameNormalizer
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(name.Length);
        foreach (var character in name.Normalize(NormalizationForm.FormD))
        {
            if (character is '®' or '™' or '©'
                || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(character) ? ' ' : char.ToLowerInvariant(character));
        }

        var folded = WhitespaceRun().Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
        return ArchitectureSuffix().Replace(folded, string.Empty).Trim();
    }

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRun();

    [GeneratedRegex(@"\s*[\(\[]\s*(x64|x86|amd64|arm64|64[- ]?bit|32[- ]?bit)\s*[\)\]]$")]
    private static partial Regex ArchitectureSuffix();
}
