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

    private void OnAddressListTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || vm.SelectedAddressRow is not { } row)
            return;
        vm.ShowAddressInfo(row);
    }

    private void OnAddressListPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(null).Properties.IsRightButtonPressed) return;
        if (sender is not ListBox lb || DataContext is not MainWindowViewModel vm) return;

        var item = (e.Source as Visual)?.FindAncestorOfType<ListBoxItem>();
        if (item is not { DataContext: AddressRow row }) return;

        lb.SelectedItem = row;
        vm.ShowAddressInfo(row);
    }

    // Chiusura dell'overlay dettaglio indirizzo: click sullo sfondo scuro
    // (solo sullo sfondo, non sulla scheda) o tasto Esc.
    private void OnAddressOverlayBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        if (DataContext is MainWindowViewModel vm)
            vm.AddressInfo = null;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel { AddressInfo: not null } vm)
        {
            vm.AddressInfo = null;
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }
}
