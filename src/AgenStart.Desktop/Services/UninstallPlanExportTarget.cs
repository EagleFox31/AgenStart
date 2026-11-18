using System.Text;
using Avalonia.Platform.Storage;

namespace AgenStart.Desktop.Services;

/// <summary>
/// Where an exported uninstall plan goes. It is the uninstall page's only write, always started by
/// the user, and it writes nothing but the file the user picks.
/// </summary>
public interface IUninstallPlanExportTarget
{
    /// <summary>Asks the user for a file and writes <paramref name="content"/> to it; false when cancelled.</summary>
    Task<bool> SaveAsync(string suggestedFileName, string content, CancellationToken cancellationToken = default);
}

/// <summary>Saves through the window's save-file dialog.</summary>
public sealed class StorageProviderUninstallPlanExportTarget(Func<IStorageProvider> storage, Func<string> dialogTitle)
    : IUninstallPlanExportTarget
{
    private static readonly FilePickerFileType JsonFile = new("JSON")
    {
        Patterns = ["*.json"],
        MimeTypes = ["application/json"]
    };

    public async Task<bool> SaveAsync(string suggestedFileName, string content, CancellationToken cancellationToken = default)
    {
        var provider = storage();
        if (!provider.CanSave)
        {
            return false;
        }

        var file = await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = dialogTitle(),
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "json",
            FileTypeChoices = [JsonFile],
            ShowOverwritePrompt = true
        });
        if (file is null)
        {
            return false;
        }

        await using var stream = await file.OpenWriteAsync();
        if (stream.CanSeek)
        {
            stream.SetLength(0);
        }

        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
        await writer.FlushAsync(cancellationToken);
        return true;
    }
}
