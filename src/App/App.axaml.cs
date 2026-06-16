using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using PalladiumWallet.App.ViewModels;
using PalladiumWallet.App.Views;

namespace PalladiumWallet.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var vm = new MainWindowViewModel();

        // Desktop (Windows/Linux): classic window. Mobile (Android): single
        // view. Same shared UI (MainView) and same ViewModel.
        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow { DataContext = vm };
                break;
            case ISingleViewApplicationLifetime singleView:
                singleView.MainView = new MainView { DataContext = vm };
                break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
