using System.Buffers.Binary;
using System.Text;

namespace AgenStart.Uninstall;

/// <summary>
/// Reads the local target path of a <c>.lnk</c> file from its LinkInfo structure (MS-SHLLINK §2.3),
/// without COM and without resolving anything. Advertised (MSI) shortcuts and network targets give
/// null: the planner then simply does not match them.
/// </summary>
public static class ShellLinkParser
{
    public const int MaximumLength = 64 * 1024;

    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x1;
    private const uint HasLinkInfo = 0x2;
    private const uint VolumeIdAndLocalBasePath = 0x1;

    private static readonly Guid LinkClassId = new("00021401-0000-0000-C000-000000000046");

    public static string? ReadTarget(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize
            || BinaryPrimitives.ReadUInt32LittleEndian(data) != HeaderSize
            || new Guid(data.Slice(4, 16)) != LinkClassId)
        {
            return null;
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x14..]);
        var offset = HeaderSize;
        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (data.Length < offset + 2)
            {
                return null;
            }

            offset += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
        }

        if ((flags & HasLinkInfo) == 0 || data.Length < offset + 0x1C)
        {
            return null;
        }

        var info = data[offset..];
        var infoSize = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(info), (uint)info.Length);
        if (infoSize < 0x1C)
        {
            return null;
        }

        info = info[..infoSize];
        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
        var infoFlags = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        if ((infoFlags & VolumeIdAndLocalBasePath) == 0)
        {
            return null;
        }

        string? basePath;
        string? suffix;
        if (headerSize >= 0x24 && info.Length >= 0x24)
        {
            basePath = Unicode(info, BinaryPrimitives.ReadUInt32LittleEndian(info[0x1C..]));
            suffix = Unicode(info, BinaryPrimitives.ReadUInt32LittleEndian(info[0x20..]));
        }
        else
        {
            basePath = Ansi(info, BinaryPrimitives.ReadUInt32LittleEndian(info[0x10..]));
            suffix = Ansi(info, BinaryPrimitives.ReadUInt32LittleEndian(info[0x18..]));
        }

        if (string.IsNullOrEmpty(basePath))
        {
            return null;
        }

        return string.IsNullOrEmpty(suffix) ? basePath : basePath.TrimEnd('\\') + "\\" + suffix.TrimStart('\\');
    }

    private static string? Ansi(ReadOnlySpan<byte> info, uint offset)
    {
        if (offset == 0 || offset >= info.Length)
        {
            return null;
        }

        var slice = info[(int)offset..];
        var end = slice.IndexOf((byte)0);
        return end < 0 ? null : Encoding.Latin1.GetString(slice[..end]);
    }

    private static string? Unicode(ReadOnlySpan<byte> info, uint offset)
    {
        if (offset == 0 || offset >= info.Length)
        {
            return null;
        }

        var slice = info[(int)offset..];
        for (var index = 0; index + 1 < slice.Length; index += 2)
        {
            if (slice[index] == 0 && slice[index + 1] == 0)
            {
                return Encoding.Unicode.GetString(slice[..index]);
            }
        }

        return null;
    }
}
