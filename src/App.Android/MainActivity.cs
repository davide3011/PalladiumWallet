using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace PalladiumWallet.Mobile;

// Activity di avvio. La configurazione dell'app (tipo App, font) è nella
// MainApplication (AvaloniaAndroidApplication<App>). Qui basta il launcher.
[Activity(
    Label = "Palladium Wallet",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
