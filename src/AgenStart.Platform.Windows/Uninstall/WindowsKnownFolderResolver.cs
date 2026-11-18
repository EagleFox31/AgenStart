using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Uninstall;

/// <summary>
/// Resolves known folders with <c>SHGetKnownFolderPath</c>, so redirected and localised folders
/// (a Documents folder moved to D:\, a "Programmes" folder) are seen where they really are.
/// Read-only: the folder is never created (no <c>KF_FLAG_CREATE</c>).
/// </summary>
public sealed class WindowsKnownFolderResolver : IKnownFolderResolver
{
    private static readonly Dictionary<KnownFolder, Guid> FolderIds = new()
    {
        [KnownFolder.UserProfile] = new("5E6C858F-0E22-4760-9AFE-EA3317B67173"),
        [KnownFolder.UserProfiles] = new("0762D272-C50A-4BB0-A382-697DCD729B80"),
        [KnownFolder.LocalAppData] = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091"),
        [KnownFolder.RoamingAppData] = new("3EB685DB-65F9-4CF6-A03A-E3EF65729F3D"),
        [KnownFolder.LocalLow] = new("A520A1A4-1780-4FF6-BD18-167343C5AF16"),
        [KnownFolder.ProgramData] = new("62AB5D82-FDC1-4DC3-A9DD-070D1D495D97"),
        [KnownFolder.Documents] = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7"),
        [KnownFolder.Desktop] = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641"),
        [KnownFolder.Downloads] = new("374DE290-123F-4565-9164-39C4925E467B"),
        [KnownFolder.SavedGames] = new("4C5C32FF-BB9D-43B0-B5B4-2D72E54EAAA4"),
        [KnownFolder.StartMenuPrograms] = new("A77F5D77-2E2B-44C3-A6A2-ABA601054A51"),
        [KnownFolder.CommonStartMenuPrograms] = new("0139D44E-6AFE-49F2-8690-3DAFCAE6FFB8"),
        [KnownFolder.PublicDesktop] = new("C4AA340D-F20F-4863-AFEF-F87EF2E6BA25"),
        [KnownFolder.PublicDocuments] = new("ED4824AF-DCE4-45A8-81E2-FC7965083634"),
        [KnownFolder.Windows] = new("F38BF404-1D43-42F2-9305-67DE0B28FC23"),
        [KnownFolder.ProgramFiles] = new("905E63B6-C1BF-494E-B29C-65B732D3D21A"),
        [KnownFolder.ProgramFilesX86] = new("7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E"),
        [KnownFolder.CommonFiles] = new("F7F1ED05-9F6D-47A2-AAAE-29D317C6F066"),
        [KnownFolder.CommonFilesX86] = new("DE974D24-D9C6-4D3E-BF91-F4455120B917")
    };

    private static readonly Dictionary<KnownFolder, string> EnvironmentFolders = new()
    {
        [KnownFolder.OneDrive] = "OneDrive",
        [KnownFolder.OneDriveConsumer] = "OneDriveConsumer",
        [KnownFolder.OneDriveCommercial] = "OneDriveCommercial"
    };

    public string? Resolve(KnownFolder folder)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        if (folder == KnownFolder.Temp)
        {
            return Existing(Path.GetTempPath());
        }

        if (EnvironmentFolders.TryGetValue(folder, out var variable))
        {
            return Existing(Environment.GetEnvironmentVariable(variable));
        }

        return FolderIds.TryGetValue(folder, out var id) ? Existing(GetKnownFolderPath(id)) : null;
    }

    private static string? Existing(string? path) =>
        WindowsPath.Normalize(path) is { } normalized && Directory.Exists(normalized) ? normalized : null;

    [SupportedOSPlatform("windows")]
    private static string? GetKnownFolderPath(Guid id)
    {
        var buffer = IntPtr.Zero;
        try
        {
            return SHGetKnownFolderPath(ref id, 0, IntPtr.Zero, out buffer) == 0
                ? Marshal.PtrToStringUni(buffer)
                : null;
        }
        finally
        {
            // The shell allocates the buffer even on failure; the caller always frees it.
            Marshal.FreeCoTaskMem(buffer);
        }
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(ref Guid folderId, uint flags, IntPtr token, out IntPtr path);
}
