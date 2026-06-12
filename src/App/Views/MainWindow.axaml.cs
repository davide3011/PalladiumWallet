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

    private void OnServerOverlayBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        if (DataContext is MainWindowViewModel vm)
            vm.IsServerSettingsOpen = false;
    }

    private async void OnChooseDataFolderClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm)
            return;

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Cartella dati Palladium Wallet",
            AllowMultiple = false,
        });

        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.ApplyDataLocation(path);
    }

    private void OnConnectionStatusTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
            vm.IsServerSettingsOpen = true;
    }

    private void OnSettingsOverlayBackdropTapped(object? sender, TappedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender)) return;
        if (DataContext is MainWindowViewModel vm)
            vm.IsSettingsOpen = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is MainWindowViewModel vm)
        {
            if (vm.AddressInfo is not null) { vm.AddressInfo = null; e.Handled = true; return; }
            if (vm.IsServerSettingsOpen) { vm.IsServerSettingsOpen = false; e.Handled = true; return; }
            if (vm.IsSettingsOpen) { vm.IsSettingsOpen = false; e.Handled = true; return; }
        }
        base.OnKeyDown(e);
    }
}
