using System.Collections.Generic;
using System.ComponentModel;

namespace PalladiumWallet.App.Localization;

/// <summary>
/// Localizzazione UI (blueprint §14): dizionario chiave → [it, en], con
/// indicizzatore bindabile da XAML ({Binding Loc[chiave]}). Al cambio lingua
/// notifica "Item[]" e tutte le binding si aggiornano.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static readonly string[] Languages = ["it", "en"];
    public static readonly string[] LanguageNames = ["Italiano", "English"];

    public string Language { get; private set; } = "it";

    public event PropertyChangedEventHandler? PropertyChanged;

    public void SetLanguage(string language)
    {
        if (Language == language || System.Array.IndexOf(Languages, language) < 0)
            return;
        Language = language;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Language)));
    }

    public string this[string key] =>
        Strings.TryGetValue(key, out var values)
            ? values[Language == "en" ? 1 : 0]
            : key;

    public static string Tr(string key) => Instance[key];

    private static readonly Dictionary<string, string[]> Strings = new()
    {
        // Menu
        ["menu.file"] = ["_File", "_File"],
        ["menu.file.new"] = ["Nuovo / ripristina wallet…", "New / restore wallet…"],
        ["menu.file.open"] = ["Apri wallet da file…", "Open wallet from file…"],
        ["menu.file.close"] = ["Chiudi wallet", "Close wallet"],
        ["menu.net"] = ["_Rete", "_Network"],
        ["menu.net.discover"] = ["Cerca altri server (peer)", "Discover servers (peers)"],
        ["menu.net.resetcerts"] = ["Reset certificati SSL", "Reset SSL certificates"],
        ["menu.settings"] = ["_Impostazioni", "_Settings"],
        ["settings.unit.short"] = ["Unità", "Unit"],

        // Wizard
        ["wiz.net"] = ["Rete:", "Network:"],
        ["wiz.open.btn"] = ["Apri il wallet esistente", "Open existing wallet"],
        ["wiz.new.btn"] = ["Crea un nuovo wallet", "Create a new wallet"],
        ["wiz.restore.btn"] = ["Ripristina da seed", "Restore from seed"],
        ["wiz.open.title"] = ["Apri il wallet", "Open the wallet"],
        ["wiz.open.placeholder"] = ["Password del file (vuoto se non impostata)", "File password (empty if not set)"],
        ["wiz.open.ok"] = ["Apri", "Open"],
        ["wiz.seed.title"] = ["Il tuo seed (12 parole)", "Your seed (12 words)"],
        ["wiz.seed.warning"] = [
            "Scrivi le parole su carta, nell'ordine. Chi le possiede controlla i fondi; se le perdi, i fondi sono irrecuperabili.",
            "Write the words on paper, in order. Whoever holds them controls the funds; if you lose them, funds are unrecoverable."],
        ["wiz.seed.next"] = ["Le ho scritte — Avanti", "I wrote them down — Next"],
        ["wiz.confirm.title"] = ["Conferma il seed", "Confirm the seed"],
        ["wiz.confirm.placeholder"] = ["Reinserisci le 12 parole separate da spazi", "Re-enter the 12 words separated by spaces"],
        ["wiz.words.title"] = ["Ripristina da seed", "Restore from seed"],
        ["wiz.words.placeholder"] = ["Mnemonica BIP39 (12 o 24 parole separate da spazi)", "BIP39 mnemonic (12 or 24 words separated by spaces)"],
        ["wiz.passphrase.title"] = ["Passphrase opzionale", "Optional passphrase"],
        ["wiz.passphrase.placeholder"] = ["Lascia vuoto per non usarla", "Leave empty to skip"],
        ["wiz.password.title"] = ["Password del file wallet", "Wallet file password"],
        ["wiz.password.placeholder"] = ["Consigliata (vuoto = file in chiaro su disco)", "Recommended (empty = plaintext file on disk)"],
        ["wiz.password.create"] = ["Crea il wallet", "Create wallet"],
        ["wiz.back"] = ["Indietro", "Back"],
        ["wiz.next"] = ["Avanti", "Next"],

        // Pannello wallet
        ["wallet.close"] = ["Chiudi wallet", "Close wallet"],
        ["wallet.server"] = ["Server:", "Server:"],
        ["wallet.connect"] = ["Connetti e sincronizza", "Connect and sync"],
        ["wallet.manual"] = ["oppure host:porta manuale", "or manual host:port"],
        ["wallet.discover"] = ["Cerca altri server", "Discover servers"],
        ["wallet.resetcert"] = ["Reset cert.", "Reset certs"],
        ["tab.receive"] = ["Ricevi", "Receive"],
        ["tab.history"] = ["Storico", "History"],
        ["tab.addresses"] = ["Indirizzi", "Addresses"],
        ["tab.send"] = ["Invia", "Send"],
        ["receive.next"] = ["Prossimo indirizzo non usato:", "Next unused address:"],
        ["receive.hint"] = [
            "Ogni pagamento ricevuto qui comparirà nello storico alla prossima sincronizzazione.",
            "Payments received here will appear in the history at the next synchronization."],
        ["addr.type"] = ["Tipo", "Type"],
        ["addr.index"] = ["Indice", "Index"],
        ["addr.address"] = ["Indirizzo", "Address"],
        ["addr.balance"] = ["Saldo", "Balance"],
        ["addr.receive"] = ["ricezione", "receive"],
        ["addr.change"] = ["change", "change"],
        ["send.to"] = ["Indirizzo destinatario", "Recipient address"],
        ["send.amount"] = ["Importo", "Amount"],
        ["send.all"] = ["Invia tutto", "Send all"],
        ["send.feerate"] = ["fee sat/vB:", "fee sat/vB:"],
        ["send.prepare"] = ["Prepara transazione", "Prepare transaction"],
        ["send.confirm"] = ["CONFERMA E TRASMETTI", "CONFIRM AND BROADCAST"],

        // Stato connessione
        ["conn.none"] = ["non connesso", "not connected"],
        ["conn.disconnected"] = ["disconnesso", "disconnected"],
        ["conn.reconnecting"] = ["riconnessione…", "reconnecting…"],
        ["conn.error"] = ["errore di connessione", "connection error"],
        ["conn.certchanged"] = ["certificato cambiato", "certificate changed"],
        ["conn.connectedto"] = ["connesso a", "connected to"],
        ["conn.connectingto"] = ["connessione a", "connecting to"],

        // Messaggi di stato principali
        ["msg.welcome.existing"] = [
            "Trovato un wallet esistente su questa rete: aprilo, oppure creane un altro.",
            "Found an existing wallet on this network: open it, or create another one."],
        ["msg.welcome.new"] = [
            "Benvenuto: crea un nuovo wallet o ripristina da seed.",
            "Welcome: create a new wallet or restore from seed."],
        ["msg.open.password"] = [
            "Inserisci la password del file (lascia vuoto se non impostata).",
            "Enter the file password (leave empty if not set)."],
        ["msg.seed.write"] = [
            "Scrivi le 12 parole SU CARTA, nell'ordine. Sono l'unico backup del wallet.",
            "Write the 12 words ON PAPER, in order. They are the only backup of the wallet."],
        ["msg.seed.retype"] = [
            "Reinserisci le 12 parole per confermare di averle scritte.",
            "Re-enter the 12 words to confirm you wrote them down."],
        ["msg.seed.mismatch"] = [
            "Le parole non corrispondono: ricontrolla quello che hai scritto su carta.",
            "The words do not match: check what you wrote on paper."],
        ["msg.words.enter"] = [
            "Inserisci la mnemonica BIP39 (12 o 24 parole separate da spazi).",
            "Enter the BIP39 mnemonic (12 or 24 words separated by spaces)."],
        ["msg.words.invalid"] = [
            "Mnemonica non valida (parole o checksum errati): ricontrolla.",
            "Invalid mnemonic (wrong words or checksum): check again."],
        ["msg.passphrase.info"] = [
            "Passphrase BIP39 opzionale: cambia completamente il wallet. Se la usi, annotala A PARTE dal seed; se la perdi i fondi sono irrecuperabili. Lascia vuoto per non usarla.",
            "Optional BIP39 passphrase: it derives a completely different wallet. If you use it, note it SEPARATELY from the seed; if lost, funds are unrecoverable. Leave empty to skip."],
        ["msg.password.info"] = [
            "Password di cifratura del file wallet su disco (consigliata). Non sostituisce il seed: serve solo a proteggere il file.",
            "Encryption password for the wallet file on disk (recommended). It does not replace the seed: it only protects the file."],
        ["msg.wrongpassword"] = ["Password errata.", "Wrong password."],
        ["msg.opened"] = ["Wallet aperto: connessione al server…", "Wallet opened: connecting to server…"],
        ["msg.synced"] = ["Sincronizzato", "Synchronized"],
        ["msg.synced.detail"] = [
            "transazioni verificate SPV. Aggiornamento in tempo reale attivo.",
            "SPV-verified transactions. Real-time updates active."],
        ["msg.height"] = ["altezza", "height"],
        ["msg.pending"] = ["in attesa di conferma", "pending confirmation"],
        ["msg.notspendable"] = ["non ancora spendibile", "not yet spendable"],
        ["msg.settings.saved"] = ["Impostazioni salvate.", "Settings saved."],
        ["msg.certreset"] = [
            "Certificati SSL azzerati: riprova la connessione.",
            "SSL certificates cleared: retry the connection."],
        ["msg.error"] = ["Errore", "Error"],

        // Finestra impostazioni
        ["settings.title"] = ["Impostazioni", "Settings"],
        ["settings.language"] = ["Lingua", "Language"],
        ["settings.unit"] = ["Unità degli importi", "Amount unit"],
        ["settings.ok"] = ["Salva", "Save"],
        ["settings.cancel"] = ["Annulla", "Cancel"],
    };
}
