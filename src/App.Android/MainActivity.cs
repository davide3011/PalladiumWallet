using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using AvaloniaApp = PalladiumWallet.App.App;

namespace PalladiumWallet.Mobile;

[Activity(
    Label = "Palladium Wallet",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    internal const int ScanRequestCode = 9001;
    internal static TaskCompletionSource<string?>? ScanTcs;
    internal static MainActivity? Current;
    private bool _wasPaused;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
    }

    protected override void OnPause()
    {
        base.OnPause();
        _wasPaused = true;
    }

    protected override void OnResume()
    {
        base.OnResume();
        if (!_wasPaused) return;
        _wasPaused = false;
        // The TCP socket can die silently while the screen was off/locked (Doze,
        // mobile radio suspend, NAT timeout) without the app ever observing the
        // failure. Force an immediate health check instead of waiting for the next
        // 20s keep-alive tick, which may itself have been suspended for longer than
        // the lock.
        if (AvaloniaApp.MainViewModel is { } vm)
            _ = vm.CheckConnectionOnResumeAsync();
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ScanRequestCode)
        {
            var text = resultCode == Result.Ok ? data?.GetStringExtra(ScannerActivity.ResultKey) : null;
            ScanTcs?.TrySetResult(text);
            ScanTcs = null;
        }
    }
}
