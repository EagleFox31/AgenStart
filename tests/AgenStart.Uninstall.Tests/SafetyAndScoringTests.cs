using AgenStart.Uninstall;

namespace AgenStart.Uninstall.Tests;

public sealed class WindowsPathTests
{
    [Theory]
    [InlineData(@"c:\Program Files\App\", @"C:\Program Files\App")]
    [InlineData(@"C:/Users/alex/AppData", @"C:\Users\alex\AppData")]
    [InlineData(@"\\?\C:\Games\Big", @"C:\Games\Big")]
    [InlineData(@"\\?\UNC\server\share\dir", @"\\server\share\dir")]
    [InlineData(@"""C:\Quoted Path""", @"C:\Quoted Path")]
    [InlineData(@"C:\", @"C:\")]
    [InlineData(@"C:\App. ", @"C:\App")]
    public void NormalizesAbsolutePaths(string input, string expected)
    {
        Assert.Equal(expected, WindowsPath.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"relative\path")]
    [InlineData(@"C:relative")]
    [InlineData(@"C:\App\..\Windows")]
    [InlineData(@"C:\App\.\x")]
    [InlineData(@"C:\App\*")]
    [InlineData(@"\\server")]
    public void RejectsPathsItCannotReasonAbout(string? input)
    {
        Assert.Null(WindowsPath.Normalize(input));
    }

    [Fact]
    public void ComparesCaseInsensitivelyOnWholeSegments()
    {
        Assert.True(WindowsPath.IsStrictlyUnder(@"C:\Program Files\App\bin", @"c:\program files\app"));
        Assert.False(WindowsPath.IsStrictlyUnder(@"C:\Program Files\AppX", @"C:\Program Files\App"));
        Assert.False(WindowsPath.IsStrictlyUnder(@"C:\App", @"C:\App"));
        Assert.True(WindowsPath.IsSameOrUnder(@"C:\App", @"c:\app"));
        Assert.Equal(@"C:\", WindowsPath.Parent(@"C:\App"));
        Assert.Null(WindowsPath.Parent(@"C:\"));
        Assert.Equal(@"\\server\share\", WindowsPath.Parent(@"\\server\share\dir"));
    }
}

public sealed class PathSafetyPolicyTests
{
    private static readonly FakeKnownFolderResolver Folders = new(new Dictionary<KnownFolder, string>
    {
        [KnownFolder.OneDrive] = @"C:\Users\alex\OneDrive"
    });

    private static PathSafetyPolicy Policy(string? dataDirectory = null) =>
        PathSafetyPolicy.Create(Folders, TestRules.Frozen, dataDirectory);

    [Theory]
    [InlineData(@"C:\", SafetyVerdict.Forbidden)]
    [InlineData(@"D:\", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Program Files", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\alex\AppData\Local", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\alex", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Windows\System32\drivers", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Program Files\WindowsApps\App_1.0", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\alex\AppData\Local\Packages\App_x", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\alex\AppData\Local\AgenStart\logs", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\bob\AppData\Local\App", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\bob", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Program Files (x86)\Steam", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\ProgramData\Package Cache", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users", SafetyVerdict.Forbidden)]
    [InlineData(@"C:\Users\alex\AppData", SafetyVerdict.AncestorOfForbidden)]
    [InlineData(@"C:\ProgramData\Microsoft", SafetyVerdict.AncestorOfForbidden)]
    [InlineData(@"C:\Games", SafetyVerdict.TooShallow)]
    [InlineData(@"relative", SafetyVerdict.Invalid)]
    [InlineData(@"C:\Program Files\Vendor\App", SafetyVerdict.Allowed)]
    [InlineData(@"C:\Users\alex\AppData\Local\Vendor\App", SafetyVerdict.Allowed)]
    [InlineData(@"C:\Users\Public\Desktop\App.lnk", SafetyVerdict.Allowed)]
    [InlineData(@"D:\Games\App", SafetyVerdict.Allowed)]
    public void EvaluatesPathsAgainstTheFloor(string path, SafetyVerdict expected)
    {
        Assert.Equal(expected, Policy().EvaluatePath(path));
    }

    [Fact]
    public void ProtectsTheAgenStartDataDirectoryAndItsAncestors()
    {
        var policy = Policy(@"D:\Portable\AgenStartData");

        Assert.Equal(SafetyVerdict.Forbidden, policy.EvaluatePath(@"D:\Portable\AgenStartData\state"));
        Assert.Equal(SafetyVerdict.AncestorOfForbidden, policy.EvaluatePath(@"D:\Portable"));
    }

    [Fact]
    public void FollowsRedirectedAndLocalisedKnownFolders()
    {
        var folders = new FakeKnownFolderResolver(new Dictionary<KnownFolder, string>
        {
            [KnownFolder.Documents] = @"D:\Mes documents",
            [KnownFolder.ProgramFiles] = @"C:\Programmes"
        });
        var policy = PathSafetyPolicy.Create(folders, null, null);

        Assert.Equal(SafetyVerdict.Forbidden, policy.EvaluatePath(@"D:\Mes documents"));
        Assert.Equal(SafetyVerdict.Allowed, policy.EvaluatePath(@"D:\Mes documents\Vendor"));
        Assert.Equal(SafetyVerdict.Allowed, policy.EvaluatePath(@"C:\Users\alex\Documents\Vendor\App"));
        Assert.Equal(SafetyVerdict.Forbidden, policy.EvaluatePath(@"C:\Programmes"));
        Assert.Equal(SafetyVerdict.Allowed, policy.EvaluatePath(@"C:\Programmes\Vendor"));
    }

    [Theory]
    [InlineData(@"HKCU\Software\Vendor", SafetyVerdict.Allowed)]
    [InlineData(@"HKEY_CURRENT_USER\Software\Vendor\App", SafetyVerdict.Allowed)]
    [InlineData(@"HKLM\SOFTWARE\Vendor\App", SafetyVerdict.Allowed)]
    [InlineData(@"HKLM\SOFTWARE\WOW6432Node\Vendor\App", SafetyVerdict.Allowed)]
    [InlineData(@"HKLM\SOFTWARE\Vendor", SafetyVerdict.TooShallow)]
    [InlineData(@"HKCU\Software", SafetyVerdict.TooShallow)]
    [InlineData(@"HKCU\Software\Microsoft\Office", SafetyVerdict.Forbidden)]
    [InlineData(@"HKLM\SOFTWARE\Classes\.txt", SafetyVerdict.Forbidden)]
    [InlineData(@"HKLM\SOFTWARE\Wow6432Node\Policies\Vendor", SafetyVerdict.Forbidden)]
    [InlineData(@"HKLM\SYSTEM\CurrentControlSet\Services\App", SafetyVerdict.Forbidden)]
    [InlineData(@"HKCR\App", SafetyVerdict.Invalid)]
    [InlineData(@"HKCU\Software\*", SafetyVerdict.Invalid)]
    public void EvaluatesRegistryKeys(string key, SafetyVerdict expected)
    {
        Assert.Equal(expected, Policy().EvaluateRegistryKey(key));
    }

    [Theory]
    [InlineData("acme studio", true)]
    [InlineData("app", false)]
    [InlineData("games", false)]
    [InlineData("software", false)]
    [InlineData("", false)]
    public void OnlyDistinctiveNamesIdentifyAProgram(string name, bool expected)
    {
        Assert.Equal(expected, Policy().IsDistinctiveName(name));
    }

    [Fact]
    public void TreatsOneDriveAsUserData()
    {
        Assert.True(Policy().IsUserData(@"C:\Users\alex\OneDrive\Saves"));
        Assert.False(Policy().IsUserData(@"C:\Users\alex\AppData\Local\App"));
    }
}

public sealed class ConfidenceScorerTests
{
    [Fact]
    public void UsesTheAdr0006Weights()
    {
        Assert.Equal(20, ConfidenceScorer.UninstallKeyWeight);
        Assert.Equal(4, ConfidenceScorer.PublisherMatchWeight);
        Assert.Equal(-1, ConfidenceScorer.DirectlyUnderKnownFolderWeight);
        Assert.Equal(-4, ConfidenceScorer.ExecutablesPresentWeight);
        Assert.Equal(-4, ConfidenceScorer.PublisherSharedWeight);
        Assert.Equal(-7, ConfidenceScorer.InUseWeight);
        Assert.Equal(-10, ConfidenceScorer.StoreAppWeight);
    }

    [Fact]
    public void AddsEverySignalAndExplainsIt()
    {
        var score = ConfidenceScorer.Score(new ConfidenceSignals(6, true, true, true, true, true, true, true));

        Assert.Equal(6 + 20 + 4 - 1 - 4 - 4 - 7 - 10, score.Score);
        Assert.Equal(
            ["rule", "uninstall-key", "publisher-match", "directly-under-known-folder", "executables-present", "publisher-shared", "in-use", "store-app"],
            score.Reasons.Select(reason => reason.Code));
        Assert.Equal(score.Score, score.Reasons.Sum(reason => reason.Weight));
    }

    [Theory]
    [InlineData(20, ConfidenceLevel.High)]
    [InlineData(8, ConfidenceLevel.High)]
    [InlineData(7, ConfidenceLevel.Medium)]
    [InlineData(4, ConfidenceLevel.Medium)]
    [InlineData(3, ConfidenceLevel.Low)]
    [InlineData(-5, ConfidenceLevel.Low)]
    public void MapsScoresToLevels(int score, ConfidenceLevel expected)
    {
        Assert.Equal(expected, ConfidenceScorer.LevelOf(score));
    }

    [Theory]
    [InlineData(8, DataClass.AppData, true)]
    [InlineData(8, DataClass.Cache, true)]
    [InlineData(30, DataClass.UserData, false)]
    [InlineData(7, DataClass.AppData, false)]
    [InlineData(2, DataClass.AppData, false)]
    public void PreselectsOnlyHighConfidenceNonUserData(int ruleConfidence, DataClass dataClass, bool expected)
    {
        var score = ConfidenceScorer.Score(new ConfidenceSignals(ruleConfidence, false, false, false, false, false, false, false));

        Assert.Equal(expected, ConfidenceScorer.IsPreselected(score, dataClass));
    }
}
