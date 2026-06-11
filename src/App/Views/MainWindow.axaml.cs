using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using PalladiumWallet.App.ViewModels;

namespace PalladiumWallet.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

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

    private async void OnAddressListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedAddressRow is not { } row)
            return;
        var info = new AddressInfo(vm.Loc, row.Indirizzo, row.DerivPath, row.PubKey, row.PrivKey);
        await new AddressInfoWindow(info).ShowDialog(this);
    }

    private async void OnAddressListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        if (sender is not ListBox lb || DataContext is not MainWindowViewModel vm) return;

        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item is not { DataContext: AddressRow row }) return;

        lb.SelectedItem = row;

        var info = new AddressInfo(vm.Loc, row.Indirizzo, row.DerivPath, row.PubKey, row.PrivKey);
        await new AddressInfoWindow(info).ShowDialog(this);
    }
}
