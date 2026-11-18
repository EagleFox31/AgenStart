using System.Text;

namespace AgenStart.Uninstall;

public sealed record ParsedCommand(string Executable, IReadOnlyList<string> Arguments);

/// <summary>
/// Splits a registered uninstall command the way Windows would (CommandLineToArgvW rules for the
/// arguments). An unquoted program path with spaces is resolved like CreateProcess does: the
/// shortest space-separated prefix that names an existing <c>.exe</c>. Parsing never runs anything.
/// </summary>
public static class UninstallCommandLine
{
    public static ParsedCommand? Parse(string? commandLine, Func<string, bool> fileExists)
    {
        ArgumentNullException.ThrowIfNull(fileExists);
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var text = commandLine.Trim();
        string program;
        string rest;
        if (text[0] == '"')
        {
            var end = text.IndexOf('"', 1);
            if (end < 0)
            {
                return null;
            }

            program = text[1..end];
            rest = text[(end + 1)..];
        }
        else
        {
            (program, rest) = SplitUnquotedProgram(text, fileExists);
        }

        return program.Trim().Length == 0 ? null : new ParsedCommand(program.Trim(), SplitArguments(rest));
    }

    /// <summary>Argument splitting with the CommandLineToArgvW backslash and quote rules.</summary>
    public static IReadOnlyList<string> SplitArguments(string text)
    {
        var arguments = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (character == '\\')
            {
                var start = index;
                while (index < text.Length && text[index] == '\\')
                {
                    index++;
                }

                var count = index - start;
                if (index < text.Length && text[index] == '"')
                {
                    current.Append('\\', count / 2);
                    if (count % 2 == 1)
                    {
                        current.Append('"');
                        index++;
                    }
                }
                else
                {
                    current.Append('\\', count);
                }

                hasToken = true;
                continue;
            }

            if (character == '"')
            {
                if (inQuotes && index + 1 < text.Length && text[index + 1] == '"')
                {
                    current.Append('"');
                    index += 2;
                    continue;
                }

                inQuotes = !inQuotes;
                hasToken = true;
                index++;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (hasToken)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                index++;
                continue;
            }

            current.Append(character);
            hasToken = true;
            index++;
        }

        if (hasToken)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    /// <summary>Display form of a command: the program quoted, arguments quoted when they hold spaces.</summary>
    public static string Format(string executable, IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var parts = new List<string> { Quote(executable, always: executable.Contains(' ', StringComparison.Ordinal)) };
        parts.AddRange(arguments.Select(argument => Quote(argument, always: false)));
        return string.Join(' ', parts);
    }

    private static string Quote(string value, bool always)
    {
        if (!always && value.Length > 0 && value.IndexOfAny([' ', '\t', '"']) < 0)
        {
            return value;
        }

        if (value.StartsWith("_?=", StringComparison.Ordinal))
        {
            // NSIS reads _?= raw to the end of the line: it must stay unquoted.
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static (string Program, string Remainder) SplitUnquotedProgram(string text, Func<string, bool> fileExists)
    {
        var firstSpace = text.IndexOfAny([' ', '\t']);
        if (firstSpace < 0)
        {
            return (text, string.Empty);
        }

        var candidateEnd = firstSpace;
        while (candidateEnd > 0)
        {
            var candidate = text[..candidateEnd];
            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && fileExists(candidate))
            {
                return (candidate, text[candidateEnd..]);
            }

            var next = text.IndexOf(' ', candidateEnd + 1);
            candidateEnd = next;
        }

        var exeEnd = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (exeEnd > 0 && (exeEnd + 4 == text.Length || char.IsWhiteSpace(text[exeEnd + 4])))
        {
            return (text[..(exeEnd + 4)], text[(exeEnd + 4)..]);
        }

        return (text[..firstSpace], text[firstSpace..]);
    }
}
