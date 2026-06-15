using System;
using System.Threading.Tasks;

namespace PalladiumWallet.App.Services;

/// <summary>
/// Seam per servizi specifici della piattaforma (Android/desktop).
/// Il head Android imposta i delegate in OnCreate; desktop li lascia null.
/// </summary>
public static class PlatformServices
{
    /// <summary>
    /// Apre lo scanner QR nativo e restituisce il testo raw del codice,
    /// oppure null se l'utente annulla o lo scanner non è disponibile.
    /// </summary>
    public static Func<Task<string?>>? ScanQrAsync { get; set; }
}
