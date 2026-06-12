using Avalonia.Controls;

namespace PalladiumWallet.App.Views;

/// <summary>Finestra desktop: ospita <see cref="MainView"/> (la UI condivisa con mobile).</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }
}
