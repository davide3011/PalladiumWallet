using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using PalladiumWallet.App.Services;
using PalladiumWallet.Core.Storage;

namespace PalladiumWallet.Mobile;

// In Avalonia 12 l'AppBuilder Android si configura nella sottoclasse Application
// (AvaloniaAndroidApplication<TApp>), non più nell'Activity. allowBackup=false:
// il file wallet cifrato/seed non deve finire nei backup cloud automatici.
[Application(Label = "Palladium Wallet", AllowBackup = false,
             Icon = "@mipmap/ic_launcher", RoundIcon = "@mipmap/ic_launcher_round")]
public class MainApplication : AvaloniaAndroidApplication<global::PalladiumWallet.App.App>
{
    public MainApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : base(javaReference, transfer)
    {
    }

    public override void OnCreate()
    {
        AppPaths.OverrideDataRoot = FilesDir?.AbsolutePath;

        // Registra lo scanner QR: apre ScannerActivity e ne attende il risultato.
        PlatformServices.ScanQrAsync = async () =>
        {
            var activity = MainActivity.Current;
            if (activity == null) return null;
            var tcs = new TaskCompletionSource<string?>();
            MainActivity.ScanTcs = tcs;
            activity.StartActivityForResult(
                new Intent(activity, typeof(ScannerActivity)),
                MainActivity.ScanRequestCode);
            return await tcs.Task;
        };

        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
