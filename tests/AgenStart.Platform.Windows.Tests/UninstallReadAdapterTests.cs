using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using AgenStart.Platform.Windows.Uninstall;
using AgenStart.Uninstall;

namespace AgenStart.Platform.Windows.Tests;

public sealed class UninstallReadAdapterTests
{
    [Theory]
    [InlineData(KnownFolder.UserProfile, Environment.SpecialFolder.UserProfile)]
    [InlineData(KnownFolder.LocalAppData, Environment.SpecialFolder.LocalApplicationData)]
    [InlineData(KnownFolder.RoamingAppData, Environment.SpecialFolder.ApplicationData)]
    [InlineData(KnownFolder.ProgramData, Environment.SpecialFolder.CommonApplicationData)]
    [InlineData(KnownFolder.Documents, Environment.SpecialFolder.MyDocuments)]
    [InlineData(KnownFolder.Desktop, Environment.SpecialFolder.DesktopDirectory)]
    [InlineData(KnownFolder.StartMenuPrograms, Environment.SpecialFolder.Programs)]
    [InlineData(KnownFolder.CommonStartMenuPrograms, Environment.SpecialFolder.CommonPrograms)]
    [InlineData(KnownFolder.PublicDesktop, Environment.SpecialFolder.CommonDesktopDirectory)]
    [InlineData(KnownFolder.PublicDocuments, Environment.SpecialFolder.CommonDocuments)]
    [InlineData(KnownFolder.Windows, Environment.SpecialFolder.Windows)]
    [InlineData(KnownFolder.ProgramFiles, Environment.SpecialFolder.ProgramFiles)]
    [InlineData(KnownFolder.ProgramFilesX86, Environment.SpecialFolder.ProgramFilesX86)]
    [InlineData(KnownFolder.CommonFiles, Environment.SpecialFolder.CommonProgramFiles)]
    [InlineData(KnownFolder.CommonFilesX86, Environment.SpecialFolder.CommonProgramFilesX86)]
    public void KnownFoldersMatchTheShell(KnownFolder folder, Environment.SpecialFolder special)
    {
        SkipUnlessWindows();
        var expected = Environment.GetFolderPath(special, Environment.SpecialFolderOption.DoNotVerify);
        Assert.SkipWhen(!Directory.Exists(expected), $"{special} does not exist on this machine.");

        Assert.Equal(WindowsPath.Normalize(expected), new WindowsKnownFolderResolver().Resolve(folder), ignoreCase: true);
    }

    [Fact]
    public void ResolvesTheProfileListAndTemp()
    {
        SkipUnlessWindows();
        var resolver = new WindowsKnownFolderResolver();

        Assert.Equal(WindowsPath.Parent(resolver.Resolve(KnownFolder.UserProfile)!), resolver.Resolve(KnownFolder.UserProfiles), ignoreCase: true);
        Assert.NotNull(resolver.Resolve(KnownFolder.LocalLow));
        Assert.NotNull(resolver.Resolve(KnownFolder.Temp));
    }

    [Fact]
    public void FileSystemProbeReadsMetadataWithoutFollowingAnything()
    {
        SkipUnlessWindows();
        var root = Directory.CreateTempSubdirectory("agenstart-probe-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "sub"));
            File.WriteAllBytes(Path.Combine(root, "a.bin"), new byte[123]);
            var probe = new WindowsFileSystemProbe();

            var directory = probe.GetEntry(root);
            var file = probe.GetEntry(Path.Combine(root, "a.bin"));
            var children = probe.ListChildren(root);

            Assert.Equal(FileSystemEntryKind.Directory, directory?.Kind);
            Assert.False(directory!.IsReparsePoint);
            Assert.Equal(FileSystemEntryKind.File, file?.Kind);
            Assert.Equal(123, file!.SizeBytes);
            Assert.Equal(["a.bin", "sub"], children.Select(child => Path.GetFileName(child.Path)).Order(StringComparer.Ordinal));
            Assert.Empty(probe.ListChildren(file.Path));
            Assert.Null(probe.GetEntry(Path.Combine(root, "missing")));
            Assert.Null(probe.GetEntry("relative"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ShortcutResolverReadsTheTargetOfALinkFile()
    {
        SkipUnlessWindows();
        var root = Directory.CreateTempSubdirectory("agenstart-lnk-").FullName;
        try
        {
            var link = Path.Combine(root, "App.lnk");
            File.WriteAllBytes(link, MinimalLink(@"C:\Program Files\App\app.exe"));
            var resolver = new WindowsShortcutResolver();

            Assert.Equal(@"C:\Program Files\App\app.exe", resolver.ResolveTarget(link));
            Assert.Null(resolver.ResolveTarget(Path.Combine(root, "missing.lnk")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RegistryProbeReturnsTheRealKeyPath()
    {
        SkipUnlessWindows();
        var probe = new WindowsRegistryProbe();

        var key = probe.GetKey(@"hkey_current_user\software\microsoft");

        Assert.NotNull(key);
        Assert.Equal(@"HKCU\Software\Microsoft", key.Path, ignoreCase: true);
        Assert.StartsWith(@"HKCU\", key.Path, StringComparison.Ordinal);
        Assert.Null(probe.GetKey(@"HKCU\Software\AgenStart-" + Guid.NewGuid().ToString("N")));
        Assert.Null(probe.GetKey(@"HKCR\.txt"));
    }

    [Fact]
    public void ProcessInspectorSeesThisProcessWithItsImage()
    {
        SkipUnlessWindows();

        var self = Assert.Single(new WindowsProcessInspector().ListProcesses(), process => process.ProcessId == Environment.ProcessId);

        Assert.Equal(Environment.ProcessPath, self.ImagePath, ignoreCase: true);
    }

    [Fact]
    public void TheReadPortsFactoryOnlyBuildsReadAdapters()
    {
        var ports = WindowsUninstallReadPorts.Create();

        Assert.IsType<WindowsKnownFolderResolver>(ports.Folders);
        Assert.IsType<WindowsFileSystemProbe>(ports.Files);
        Assert.IsType<WindowsRegistryProbe>(ports.Registry);
        Assert.IsType<WindowsShortcutResolver>(ports.Shortcuts);
        Assert.IsType<WindowsProcessInspector>(ports.Processes);
    }

    [Fact]
    public void UninstallSourcesCallNoWriteDeleteKillOrLaunchApi()
    {
        var forbidden = new Regex(
            @"\.Kill\(|\.Delete\(|DeleteSubKey|DeleteValue|SetValue\(|CreateSubKey|writable:\s*true|Process\.Start|ProcessStartInfo|"
            + @"File\.(Write|Append|Move|Copy|Create|Replace|Open\()|Directory\.(Move|CreateDirectory)|FileAccess\.(Write|ReadWrite)|"
            + @"FileMode\.(Create|Append|Truncate|OpenOrCreate)|FileSystem\.DeleteFile|SHFileOperation|IFileOperation|TerminateProcess|"
            + @"PROCESS_TERMINATE|ShellExecute|CreateProcess\(",
            RegexOptions.CultureInvariant);
        var sources = new[] { Path.Combine("src", "AgenStart.Uninstall"), Path.Combine("src", "AgenStart.Platform.Windows", "Uninstall") }
            .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(RepositoryRoot(), folder), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(sources);
        Assert.All(sources, file => Assert.DoesNotMatch(forbidden, File.ReadAllText(file)));
    }

    private static void SkipUnlessWindows() => Assert.SkipUnless(OperatingSystem.IsWindows(), "These adapters read the live Windows machine.");

    private static string RepositoryRoot([CallerFilePath] string path = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, "..", ".."));

    /// <summary>A minimal MS-SHLLINK file: header, no IDList, a LinkInfo with an ANSI local base path.</summary>
    private static byte[] MinimalLink(string target)
    {
        var path = Encoding.Latin1.GetBytes(target + "\0");
        var bytes = new byte[0x4C + 0x1C + path.Length + 1];
        BitConverter.GetBytes(0x4C).CopyTo(bytes, 0);
        new Guid("00021401-0000-0000-C000-000000000046").ToByteArray().CopyTo(bytes, 4);
        BitConverter.GetBytes(0x2u).CopyTo(bytes, 0x14);
        const int info = 0x4C;
        BitConverter.GetBytes(0x1C + path.Length + 1).CopyTo(bytes, info);
        BitConverter.GetBytes(0x1C).CopyTo(bytes, info + 4);
        BitConverter.GetBytes(0x1u).CopyTo(bytes, info + 8);
        BitConverter.GetBytes(0x1C).CopyTo(bytes, info + 0x10);
        BitConverter.GetBytes(0x1C + path.Length).CopyTo(bytes, info + 0x18);
        path.CopyTo(bytes, info + 0x1C);
        return bytes;
    }
}
