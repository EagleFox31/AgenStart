using System.Security.Cryptography;
using System.Text;

namespace AgenStart.Uninstall;

/// <summary>
/// Stable program identity: <c>{source}:{hash}</c>, where the hash covers the source and the key
/// path (case-insensitive). Source tokens are an explicit mapping, never an enum name (LL-003).
/// </summary>
public static class InstalledProgramId
{
    public static string Create(ProgramSource source, string keyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyPath);

        var token = SourceToken(source);
        var input = $"{token}|{keyPath.Trim().ToUpperInvariant()}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return $"{token}:{Convert.ToHexStringLower(hash)[..32]}";
    }

    public static string SourceToken(ProgramSource source) => source switch
    {
        ProgramSource.Registry => "registry",
        ProgramSource.WinGet => "winget",
        ProgramSource.Msix => "msix",
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown program source.")
    };
}
