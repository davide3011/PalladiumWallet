using System;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
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
        // Storage sandbox dell'app: wallet, configurazione e certificati vivono
        // qui. Impostato prima dell'init Avalonia (che crea il ViewModel e decide
        // se mostrare lo step "scegli cartella dati" del wizard).
        AppPaths.OverrideDataRoot = FilesDir?.AbsolutePath;
        base.OnCreate();
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) =>
        base.CustomizeAppBuilder(builder).WithInterFont();
}
