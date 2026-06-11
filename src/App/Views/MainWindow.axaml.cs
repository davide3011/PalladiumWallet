using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PalladiumWallet.App.ViewModels;

namespace PalladiumWallet.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    /// <summary>File → Apri wallet da file (il picker richiede il TopLevel, da qui).</summary>
    private async void OnOpenWalletFileClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Apri file wallet",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Wallet Palladium") { Patterns = ["*.wallet.json", "*.json"] },
            ],
        });

        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.OpenFromPath(path);
    }
}
