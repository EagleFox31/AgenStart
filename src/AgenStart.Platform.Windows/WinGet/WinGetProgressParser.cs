using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace AgenStart.Platform.Windows.WinGet;

/// <summary>A download progress sample read from one WinGet output line.</summary>
public readonly record struct WinGetDownloadProgress(
    long? BytesDownloaded,
    long? BytesRequired,
    double Fraction);

/// <summary>Streams a process output as lines, splitting on <c>\r</c> and <c>\n</c>.</summary>
public static class WinGetOutputLines
{
    private const int BufferSize = 1024;

    /// <summary>
    /// Reads <paramref name="reader"/> to the end, hands every non-blank line to <paramref name="onLine"/>
    /// as soon as its terminator arrives, and returns the whole text unchanged.
    /// </summary>
    public static async Task<string> PumpAsync(
        TextReader reader,
        Action<string> onLine,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(onLine);

        var all = new StringBuilder();
        var line = new StringBuilder();
        var buffer = new char[BufferSize];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            all.Append(buffer, 0, read);
            for (var index = 0; index < read; index++)
            {
                var character = buffer[index];
                if (character is '\r' or '\n')
                {
                    Flush(line, onLine);
                }
                else
                {
                    line.Append(character);
                }
            }
        }

        Flush(line, onLine);
        return all.ToString();
    }

    private static void Flush(StringBuilder line, Action<string> onLine)
    {
        if (line.Length == 0)
        {
            return;
        }

        var text = line.ToString();
        line.Clear();
        if (!string.IsNullOrWhiteSpace(text))
        {
            onLine(text);
        }
    }
}

/// <summary>
/// Reads WinGet progress lines such as <c>"  ████▒▒▒▒  38.0 MB / 104 MB"</c> or <c>"  ████▒▒▒▒  42%"</c>.
/// Pure and culture-independent: sizes use 1024 multiples, as WinGet prints them.
/// </summary>
public static partial class WinGetProgressParser
{
    public static bool TryParse(string? line, out WinGetDownloadProgress progress)
    {
        progress = default;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var text = AnsiEscape().Replace(line, string.Empty);

        var bytes = ByteProgress().Match(text);
        if (bytes.Success)
        {
            if (!TryReadSize(bytes.Groups["done"].Value, bytes.Groups["doneUnit"].Value, out var done) ||
                !TryReadSize(bytes.Groups["total"].Value, bytes.Groups["totalUnit"].Value, out var total) ||
                total <= 0)
            {
                return false;
            }

            done = Math.Min(done, total);
            progress = new WinGetDownloadProgress(done, total, (double)done / total);
            return true;
        }

        var percent = PercentProgress().Match(text);
        if (percent.Success &&
            TryReadNumber(percent.Groups["value"].Value, out var value) &&
            value is >= 0 and <= 100)
        {
            progress = new WinGetDownloadProgress(null, null, value / 100d);
            return true;
        }

        return false;
    }

    private static bool TryReadSize(string number, string unit, out long bytes)
    {
        bytes = 0;
        if (!TryReadNumber(number, out var value))
        {
            return false;
        }

        var multiplier = char.ToUpperInvariant(unit[0]) switch
        {
            'K' => 1024d,
            'M' => 1024d * 1024,
            'G' => 1024d * 1024 * 1024,
            'T' => 1024d * 1024 * 1024 * 1024,
            _ => 1d
        };

        bytes = (long)Math.Round(value * multiplier);
        return true;
    }

    private static bool TryReadNumber(string text, out double value) =>
        double.TryParse(
            text.Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out value);

    [GeneratedRegex(@"\x1B\[[0-9;?]*[A-Za-z]")]
    private static partial Regex AnsiEscape();

    [GeneratedRegex(
        @"(?<![\w.,])(?<done>\d+(?:[.,]\d+)?)\s*(?<doneUnit>[KMGT]?i?B|[KMGT]?o)\s*/\s*(?<total>\d+(?:[.,]\d+)?)\s*(?<totalUnit>[KMGT]?i?B|[KMGT]?o)(?!\w)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ByteProgress();

    [GeneratedRegex(@"(?<![\w.,%])(?<value>\d{1,3}(?:[.,]\d+)?)\s*%(?!\w)", RegexOptions.CultureInvariant)]
    private static partial Regex PercentProgress();
}

/// <summary>
/// Decides which parsed samples are worth reporting: it drops a sample that goes backwards within the
/// same transfer, and keeps at most one report per <c>minimumInterval</c> except for completion.
/// A sample with a different total starts a new transfer (WinGet downloads dependencies separately).
/// </summary>
public sealed class WinGetProgressThrottle(TimeSpan minimumInterval)
{
    private WinGetDownloadProgress? _last;
    private TimeSpan _lastReportedAt;

    public bool TryAccept(WinGetDownloadProgress sample, TimeSpan now)
    {
        if (_last is { } last)
        {
            var sameTransfer = last.BytesRequired == sample.BytesRequired;
            if (sameTransfer && sample.Fraction <= last.Fraction)
            {
                return false;
            }

            if (sameTransfer && sample.Fraction < 1 && now - _lastReportedAt < minimumInterval)
            {
                return false;
            }
        }

        _last = sample;
        _lastReportedAt = now;
        return true;
    }
}
