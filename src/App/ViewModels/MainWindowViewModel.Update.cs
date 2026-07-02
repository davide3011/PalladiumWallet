using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalladiumWallet.Core.Net;

namespace PalladiumWallet.App.ViewModels;

public partial class MainWindowViewModel
{
    // ---- update-available overlay ----

    [ObservableProperty]
    private bool isUpdateAvailableOpen;

    [ObservableProperty]
    private string updateAvailableTag = "";

    public string UpdateReleaseUrl { get; private set; } = "";

    [RelayCommand]
    private void CloseUpdateAvailable() => IsUpdateAvailableOpen = false;

    /// <summary>
    /// Fire-and-forget check run once at startup. Best-effort: any failure or an
    /// up-to-date app silently does nothing (see <see cref="UpdateChecker"/>).
    /// </summary>
    private async Task CheckForUpdatesAsync()
    {
        var latest = await UpdateChecker.CheckAsync(AppVersion);
        if (latest is null) return;

        UpdateReleaseUrl = latest.HtmlUrl;
        UpdateAvailableTag = latest.Tag;
        IsUpdateAvailableOpen = true;
    }
}
