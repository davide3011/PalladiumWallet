using System;
using System.Threading.Tasks;

namespace PalladiumWallet.App.Services;

/// <summary>
/// Seam for platform-specific services (Android/desktop).
/// The Android head sets the delegates in OnCreate; desktop leaves them null.
/// </summary>
public static class PlatformServices
{
    /// <summary>
    /// Opens the native QR scanner and returns the raw code text,
    /// or null if the user cancels or the scanner is unavailable.
    /// </summary>
    public static Func<Task<string?>>? ScanQrAsync { get; set; }
}
