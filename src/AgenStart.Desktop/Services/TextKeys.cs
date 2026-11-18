namespace AgenStart.Desktop.Services;

/// <summary>
/// String keys read from C#. XAML uses the same dictionaries through DynamicResource.
/// A test checks that every constant exists in Strings/en.axaml (plural keys as <c>.One</c>/<c>.Other</c>).
/// </summary>
public static class TextKeys
{
    public const string HistoryDateFormat = "History.DateFormat";
    public const string HistoryOutcomeFailed = "History.Outcome.Failed";
    public const string HistoryOutcomeCancelled = "History.Outcome.Cancelled";
    public const string HistoryOutcomeSkipped = "History.Outcome.Skipped";
    public const string HistoryOutcomeSuccess = "History.Outcome.Success";
    public const string HistoryApplicationSummary = "History.ApplicationSummary";
    public const string HistoryInstalledCount = "History.InstalledCount";
    public const string HistoryFailedCount = "History.FailedCount";

    public const string SettingsStatusStoredOnDevice = "Settings.Status.StoredOnDevice";
    public const string SettingsStatusSaved = "Settings.Status.Saved";
    public const string SettingsStatusSaveFailed = "Settings.Status.SaveFailed";
    public const string SettingsStatusNoAccess = "Settings.Status.NoAccess";
    public const string SettingsStatusCleared = "Settings.Status.Cleared";
}
