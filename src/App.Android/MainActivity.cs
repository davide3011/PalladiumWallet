using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;

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

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Current = this;
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
