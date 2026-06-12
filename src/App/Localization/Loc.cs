using System.Collections.Generic;

namespace PalladiumWallet.App.Localization;

/// <summary>
/// Localizzazione UI: dizionario chiave → traduzioni per lingua, con
/// indicizzatore bindabile da XAML ({Binding Loc[chiave]}). Al cambio lingua
/// il ViewModel sostituisce l'istanza così Avalonia rivaluta tutte le binding.
/// </summary>
public sealed class Loc
{
    public static Loc Instance { get; private set; } = new();

    public static readonly string[] Languages = ["it", "en", "es", "fr", "pt", "de"];
    public static readonly string[] LanguageNames = ["Italiano", "English", "Español", "Français", "Português", "Deutsch"];

    public string Language { get; private set; } = "en";

    private Loc() { }
    private Loc(string language) { Language = language; }

    /// <summary>
    /// Crea una nuova istanza con la lingua specificata e aggiorna il singleton
    /// usato da <see cref="Tr"/>. Il ViewModel assegna questa istanza alla
    /// propria property Loc così Avalonia vede un riferimento diverso e
    /// rivaluta tutte le binding {Binding Loc[chiave]}.
    /// </summary>
    internal static Loc SwitchTo(string language)
    {
        if (System.Array.IndexOf(Languages, language) < 0) language = "en";
        var loc = new Loc(language);
        Instance = loc;
        return loc;
    }

    public string this[string key] =>
        Strings.TryGetValue(key, out var values)
            ? values[System.Math.Max(0, System.Array.IndexOf(Languages, Language))]
            : key;

    public static string Tr(string key) => Instance[key];

    private static readonly Dictionary<string, string[]> Strings = new()
    {
        // Menu                                                it                                          en                              es                                      fr                                          pt                                          de
        ["menu.file"]             = ["_File",                                                             "_File",                        "_Archivo",                             "_Fichier",                                 "_Arquivo",                                 "_Datei"],
        ["menu.file.new"]         = ["Nuovo / ripristina wallet…",                                        "New / restore wallet…",        "Nuevo / restaurar wallet…",            "Nouveau / restaurer le wallet…",           "Novo / restaurar carteira…",               "Neu / Wallet wiederherstellen…"],
        ["menu.file.open"]        = ["Apri wallet da file…",                                              "Open wallet from file…",       "Abrir wallet desde archivo…",          "Ouvrir le wallet depuis un fichier…",      "Abrir carteira de arquivo…",               "Wallet aus Datei öffnen…"],
        ["menu.file.close"]       = ["Chiudi wallet",                                                     "Close wallet",                 "Cerrar wallet",                        "Fermer le wallet",                         "Fechar carteira",                          "Wallet schließen"],
        ["menu.net"]              = ["_Rete",                                                             "_Network",                     "_Red",                                 "_Réseau",                                  "_Rede",                                    "_Netzwerk"],
        ["menu.net.discover"]     = ["Cerca altri server (peer)",                                         "Discover servers (peers)",     "Buscar otros servidores (peers)",      "Rechercher d'autres serveurs (pairs)",     "Procurar outros servidores (peers)",        "Weitere Server suchen (Peers)"],
        ["menu.net.resetcerts"]   = ["Reset certificati SSL",                                             "Reset SSL certificates",       "Restablecer certificados SSL",         "Réinitialiser les certificats SSL",        "Redefinir certificados SSL",               "SSL-Zertifikate zurücksetzen"],
        ["menu.settings"]         = ["_Impostazioni",                                                     "_Settings",                    "_Configuración",                       "_Paramètres",                              "_Configurações",                           "_Einstellungen"],
        ["settings.unit.short"]   = ["Unità",                                                             "Unit",                         "Unidad",                               "Unité",                                    "Unidade",                                  "Einheit"],

        // Wizard
        ["wiz.data.title"]        = ["Dove salvare i dati",                                               "Where to store data",          "Dónde guardar los datos",              "Où enregistrer les données",               "Onde salvar os dados",                     "Wo Daten gespeichert werden"],
        ["wiz.data.info"]         = [
            "Scegli la cartella in cui salvare wallet, configurazione e certificati. Puoi usare il percorso predefinito o sceglierne uno tuo.",
            "Choose the folder where wallets, configuration and certificates are stored. Use the default path or pick your own.",
            "Elige la carpeta donde se guardarán wallets, configuración y certificados. Usa la ruta predeterminada o elige la tuya.",
            "Choisissez le dossier où enregistrer les wallets, la configuration et les certificats. Utilisez le chemin par défaut ou le vôtre.",
            "Escolha a pasta onde salvar carteiras, configuração e certificados. Use o caminho padrão ou escolha o seu.",
            "Wählen Sie den Ordner für Wallets, Konfiguration und Zertifikate. Nutzen Sie den Standardpfad oder einen eigenen."],
        ["wiz.data.default"]      = ["Percorso predefinito:",                                             "Default path:",                "Ruta predeterminada:",                 "Chemin par défaut :",                      "Caminho padrão:",                          "Standardpfad:"],
        ["wiz.data.usedefault"]   = ["Usa il percorso predefinito",                                       "Use the default path",         "Usar la ruta predeterminada",          "Utiliser le chemin par défaut",            "Usar o caminho padrão",                    "Standardpfad verwenden"],
        ["wiz.data.choose"]       = ["Scegli una cartella…",                                              "Choose a folder…",             "Elegir una carpeta…",                  "Choisir un dossier…",                      "Escolher uma pasta…",                      "Ordner wählen…"],
        ["wiz.net"]               = ["Rete:",                                                             "Network:",                     "Red:",                                 "Réseau :",                                 "Rede:",                                    "Netzwerk:"],
        ["wiz.open.btn"]          = ["Apri il wallet esistente",                                          "Open existing wallet",         "Abrir wallet existente",               "Ouvrir le wallet existant",                "Abrir carteira existente",                 "Vorhandenes Wallet öffnen"],
        ["wiz.new.btn"]           = ["Crea un nuovo wallet",                                              "Create a new wallet",          "Crear nuevo wallet",                   "Créer un nouveau wallet",                  "Criar nova carteira",                      "Neues Wallet erstellen"],
        ["wiz.restore.btn"]       = ["Ripristina da seed",                                                "Restore from seed",            "Restaurar desde semilla",              "Restaurer depuis la graine",               "Restaurar da semente",                     "Aus Seed wiederherstellen"],
        ["wiz.open.title"]        = ["Apri il wallet",                                                    "Open the wallet",              "Abrir el wallet",                      "Ouvrir le wallet",                         "Abrir a carteira",                         "Wallet öffnen"],
        ["wiz.open.placeholder"]  = ["Password del file (vuoto se non impostata)",                        "File password (empty if not set)", "Contraseña del archivo (vacío si no establecida)", "Mot de passe du fichier (vide si non défini)", "Senha do arquivo (vazio se não definida)", "Dateipasswort (leer lassen, wenn nicht gesetzt)"],
        ["wiz.open.ok"]           = ["Apri",                                                              "Open",                         "Abrir",                                "Ouvrir",                                   "Abrir",                                    "Öffnen"],
        ["wiz.seed.title"]        = ["Il tuo seed (12 parole)",                                           "Your seed (12 words)",         "Tu semilla (12 palabras)",             "Votre graine (12 mots)",                   "Sua semente (12 palavras)",                "Ihr Seed (12 Wörter)"],
        ["wiz.seed.warning"]      = [
            "Scrivi le parole su carta, nell'ordine. Chi le possiede controlla i fondi; se le perdi, i fondi sono irrecuperabili.",
            "Write the words on paper, in order. Whoever holds them controls the funds; if you lose them, funds are unrecoverable.",
            "Escribe las palabras en papel, en orden. Quien las posea controla los fondos; si las pierdes, los fondos son irrecuperables.",
            "Écrivez les mots sur papier, dans l'ordre. Celui qui les possède contrôle les fonds ; si vous les perdez, les fonds sont irrécupérables.",
            "Escreva as palavras no papel, em ordem. Quem as possuir controla os fundos; se as perder, os fundos são irrecuperáveis.",
            "Schreiben Sie die Wörter auf Papier, in der richtigen Reihenfolge. Wer sie besitzt, kontrolliert die Gelder; wenn Sie sie verlieren, sind die Gelder unwiederbringlich verloren."],
        ["wiz.seed.next"]         = ["Le ho scritte — Avanti",                                            "I wrote them down — Next",     "Las anoté — Siguiente",                "Je les ai notés — Suivant",                "Eu as anotei — Próximo",                   "Ich habe sie notiert — Weiter"],
        ["wiz.confirm.title"]     = ["Conferma il seed",                                                  "Confirm the seed",             "Confirmar la semilla",                 "Confirmer la graine",                      "Confirmar a semente",                      "Seed bestätigen"],
        ["wiz.confirm.placeholder"] = ["Reinserisci le 12 parole separate da spazi",                     "Re-enter the 12 words separated by spaces", "Reingresa las 12 palabras separadas por espacios", "Ressaisissez les 12 mots séparés par des espaces", "Reinsira as 12 palavras separadas por espaços", "12 Wörter durch Leerzeichen getrennt erneut eingeben"],
        ["wiz.words.title"]       = ["Ripristina da seed",                                                "Restore from seed",            "Restaurar desde semilla",              "Restaurer depuis la graine",               "Restaurar da semente",                     "Aus Seed wiederherstellen"],
        ["wiz.words.placeholder"] = ["Mnemonica BIP39 (12 o 24 parole separate da spazi)",               "BIP39 mnemonic (12 or 24 words separated by spaces)", "Mnemónico BIP39 (12 o 24 palabras separadas por espacios)", "Mnémonique BIP39 (12 ou 24 mots séparés par des espaces)", "Mnemônico BIP39 (12 ou 24 palavras separadas por espaços)", "BIP39-Mnemonic (12 oder 24 durch Leerzeichen getrennte Wörter)"],
        ["wiz.passphrase.title"]  = ["Passphrase opzionale",                                              "Optional passphrase",          "Frase de contraseña opcional",         "Phrase de passe optionnelle",              "Frase-senha opcional",                     "Optionale Passphrase"],
        ["wiz.passphrase.placeholder"] = ["Lascia vuoto per non usarla",                                  "Leave empty to skip",          "Deja vacío para omitir",               "Laisser vide pour ignorer",                "Deixe vazio para ignorar",                 "Leer lassen zum Überspringen"],
        ["wiz.password.title"]    = ["Password del file wallet",                                          "Wallet file password",         "Contraseña del archivo wallet",        "Mot de passe du fichier wallet",           "Senha do arquivo da carteira",             "Wallet-Dateipasswort"],
        ["wiz.password.placeholder"] = ["Consigliata (vuoto = file in chiaro su disco)",                 "Recommended (empty = plaintext file on disk)", "Recomendada (vacío = archivo en texto claro en disco)", "Recommandé (vide = fichier en texte clair sur disque)", "Recomendada (vazio = arquivo em texto simples no disco)", "Empfohlen (leer = Klartextdatei auf Disk)"],
        ["wiz.password.create"]   = ["Crea il wallet",                                                    "Create wallet",                "Crear wallet",                         "Créer le wallet",                          "Criar carteira",                           "Wallet erstellen"],
        ["wiz.back"]              = ["Indietro",                                                          "Back",                         "Atrás",                                "Retour",                                   "Voltar",                                   "Zurück"],
        ["wiz.next"]              = ["Avanti",                                                            "Next",                         "Siguiente",                            "Suivant",                                  "Próximo",                                  "Weiter"],

        // Pannello wallet
        ["wallet.close"]          = ["Chiudi wallet",                                                     "Close wallet",                 "Cerrar wallet",                        "Fermer le wallet",                         "Fechar carteira",                          "Wallet schließen"],
        ["wallet.server"]         = ["Server:",                                                           "Server:",                      "Servidor:",                            "Serveur :",                                "Servidor:",                                "Server:"],
        ["wallet.connect"]        = ["Connetti e sincronizza",                                            "Connect and sync",             "Conectar y sincronizar",               "Connecter et synchroniser",                "Conectar e sincronizar",                   "Verbinden und synchronisieren"],
        ["wallet.manual"]         = ["oppure host:porta manuale",                                         "or manual host:port",          "o host:puerto manual",                 "ou hôte:port manuel",                      "ou host:porta manual",                     "oder manuell host:port"],
        ["wallet.discover"]       = ["Cerca altri server",                                                "Discover servers",             "Buscar otros servidores",              "Rechercher des serveurs",                  "Procurar servidores",                      "Server suchen"],
        ["wallet.resetcert"]      = ["Reset cert.",                                                       "Reset certs",                  "Restablecer cert.",                    "Réinit. cert.",                            "Redefinir cert.",                          "Zert. zurücksetzen"],
        ["tab.receive"]           = ["Ricevi",                                                            "Receive",                      "Recibir",                              "Recevoir",                                 "Receber",                                  "Empfangen"],
        ["tab.history"]           = ["Storico",                                                           "History",                      "Historial",                            "Historique",                               "Histórico",                                "Verlauf"],
        ["tab.addresses"]         = ["Indirizzi",                                                         "Addresses",                    "Direcciones",                          "Adresses",                                 "Endereços",                                "Adressen"],
        ["tab.send"]              = ["Invia",                                                             "Send",                         "Enviar",                               "Envoyer",                                  "Enviar",                                   "Senden"],
        ["tab.contacts"]          = ["Contatti",                                                          "Contacts",                     "Contactos",                            "Contacts",                                 "Contatos",                                 "Kontakte"],
        ["receive.next"]          = ["Prossimo indirizzo non usato:",                                     "Next unused address:",         "Próxima dirección no usada:",          "Prochaine adresse non utilisée :",         "Próximo endereço não usado:",               "Nächste ungenutzte Adresse:"],
        ["receive.hint"]          = [
            "Ogni pagamento ricevuto qui comparirà nello storico alla prossima sincronizzazione.",
            "Payments received here will appear in the history at the next synchronization.",
            "Los pagos recibidos aquí aparecerán en el historial en la próxima sincronización.",
            "Les paiements reçus ici apparaîtront dans l'historique à la prochaine synchronisation.",
            "Os pagamentos recebidos aqui aparecerão no histórico na próxima sincronização.",
            "Hier empfangene Zahlungen erscheinen beim nächsten Synchronisieren im Verlauf."],
        ["addr.type"]             = ["Tipo",                                                              "Type",                         "Tipo",                                 "Type",                                     "Tipo",                                     "Typ"],
        ["addr.index"]            = ["Indice",                                                            "Index",                        "Índice",                               "Index",                                    "Índice",                                   "Index"],
        ["addr.address"]          = ["Indirizzo",                                                         "Address",                      "Dirección",                            "Adresse",                                  "Endereço",                                 "Adresse"],
        ["addr.balance"]          = ["Saldo",                                                             "Balance",                      "Saldo",                                "Solde",                                    "Saldo",                                    "Saldo"],
        ["addr.copied"]           = ["Indirizzo copiato negli appunti",                                   "Address copied to clipboard",  "Dirección copiada al portapapeles",    "Adresse copiée dans le presse-papiers",    "Endereço copiado para a área de transferência", "Adresse in die Zwischenablage kopiert"],
        ["addr.derivpath"]        = ["Percorso di derivazione:",                                          "Derivation path:",             "Ruta de derivación:",                  "Chemin de dérivation :",                   "Caminho de derivação:",                    "Ableitungspfad:"],
        ["addr.pubkey"]           = ["Chiave pubblica:",                                                  "Public key:",                  "Clave pública:",                       "Clé publique :",                           "Chave pública:",                           "Öffentlicher Schlüssel:"],
        ["addr.privkey"]          = ["Chiave privata (WIF):",                                             "Private key (WIF):",           "Clave privada (WIF):",                 "Clé privée (WIF) :",                       "Chave privada (WIF):",                     "Privater Schlüssel (WIF):"],
        ["addr.show.privkey"]     = ["Mostra",                                                            "Show",                         "Mostrar",                              "Afficher",                                 "Mostrar",                                  "Anzeigen"],
        ["addr.hide.privkey"]     = ["Nascondi",                                                          "Hide",                         "Ocultar",                              "Masquer",                                  "Ocultar",                                  "Ausblenden"],
        ["addr.receive"]          = ["ricezione",                                                         "receive",                      "recepción",                            "réception",                                "recebimento",                              "Empfang"],
        ["addr.change"]           = ["change",                                                            "change",                       "cambio",                               "monnaie",                                  "troco",                                    "Wechselgeld"],
        ["addr.info.title"]       = ["Informazioni indirizzo",                                          "Address information",          "Información de dirección",             "Informations sur l'adresse",               "Informações do endereço",                  "Adressinformationen"],
        ["addr.close"]            = ["Chiudi",                                                          "Close",                        "Cerrar",                               "Fermer",                                   "Fechar",                                   "Schließen"],
        ["send.from.contact"]     = ["Da contatti:",                                                      "From contacts:",               "De contactos:",                        "Depuis les contacts :",                    "De contatos:",                             "Aus Kontakten:"],
        ["send.contact.hint"]     = ["seleziona per riempire l'indirizzo",                                "select to fill address",       "selecciona para rellenar la dirección", "sélectionner pour remplir l'adresse",      "selecione para preencher o endereço",      "auswählen um Adresse einzufügen"],
        ["send.to"]               = ["Indirizzo destinatario",                                            "Recipient address",            "Dirección destinataria",               "Adresse du destinataire",                  "Endereço do destinatário",                 "Empfängeradresse"],
        ["send.amount"]           = ["Importo",                                                           "Amount",                       "Importe",                              "Montant",                                  "Valor",                                    "Betrag"],
        ["send.all"]              = ["Invia tutto",                                                       "Send all",                     "Enviar todo",                          "Tout envoyer",                             "Enviar tudo",                              "Alles senden"],
        ["send.feerate"]          = ["fee sat/vB:",                                                       "fee sat/vB:",                  "tarifa sat/vB:",                       "frais sat/vB :",                           "taxa sat/vB:",                             "Gebühr sat/vB:"],
        ["send.prepare"]          = ["Prepara transazione",                                               "Prepare transaction",          "Preparar transacción",                 "Préparer la transaction",                  "Preparar transação",                       "Transaktion vorbereiten"],
        ["send.confirm"]          = ["CONFERMA E TRASMETTI",                                              "CONFIRM AND BROADCAST",        "CONFIRMAR Y TRANSMITIR",               "CONFIRMER ET DIFFUSER",                    "CONFIRMAR E TRANSMITIR",                   "BESTÄTIGEN UND SENDEN"],

        // Stato connessione
        ["conn.none"]             = ["non connesso",                                                      "not connected",                "no conectado",                         "non connecté",                             "não conectado",                            "nicht verbunden"],
        ["conn.disconnected"]     = ["disconnesso",                                                       "disconnected",                 "desconectado",                         "déconnecté",                               "desconectado",                             "getrennt"],
        ["conn.reconnecting"]     = ["riconnessione…",                                                    "reconnecting…",                "reconectando…",                        "reconnexion…",                             "reconectando…",                            "Verbindung wird wiederhergestellt…"],
        ["conn.error"]            = ["errore di connessione",                                             "connection error",             "error de conexión",                    "erreur de connexion",                      "erro de conexão",                          "Verbindungsfehler"],
        ["conn.certchanged"]      = ["certificato cambiato",                                              "certificate changed",          "certificado cambiado",                 "certificat modifié",                       "certificado alterado",                     "Zertifikat geändert"],
        ["conn.connectedto"]      = ["connesso a",                                                        "connected to",                 "conectado a",                          "connecté à",                               "conectado a",                              "verbunden mit"],
        ["conn.connectingto"]     = ["connessione a",                                                     "connecting to",                "conectando a",                         "connexion à",                              "conectando a",                             "Verbindung zu"],

        // Messaggi di stato principali
        ["msg.welcome.existing"]  = [
            "Trovato un wallet esistente su questa rete: aprilo, oppure creane un altro.",
            "Found an existing wallet on this network: open it, or create another one.",
            "Se encontró un wallet existente en esta red: ábrelo o crea otro.",
            "Un wallet existant a été trouvé sur ce réseau : ouvrez-le ou créez-en un autre.",
            "Uma carteira existente foi encontrada nesta rede: abra-a ou crie outra.",
            "Ein vorhandenes Wallet wurde in diesem Netzwerk gefunden: öffnen Sie es oder erstellen Sie ein neues."],
        ["msg.welcome.new"]       = [
            "Benvenuto: crea un nuovo wallet o ripristina da seed.",
            "Welcome: create a new wallet or restore from seed.",
            "Bienvenido: crea un nuevo wallet o restaura desde semilla.",
            "Bienvenue : créez un nouveau wallet ou restaurez depuis une graine.",
            "Bem-vindo: crie uma nova carteira ou restaure da semente.",
            "Willkommen: erstellen Sie ein neues Wallet oder stellen Sie es aus einem Seed wieder her."],
        ["msg.open.password"]     = [
            "Inserisci la password del file (lascia vuoto se non impostata).",
            "Enter the file password (leave empty if not set).",
            "Ingresa la contraseña del archivo (deja vacío si no establecida).",
            "Entrez le mot de passe du fichier (laisser vide si non défini).",
            "Digite a senha do arquivo (deixe vazio se não definida).",
            "Geben Sie das Dateipasswort ein (leer lassen, wenn nicht gesetzt)."],
        ["msg.seed.write"]        = [
            "Scrivi le 12 parole SU CARTA, nell'ordine. Sono l'unico backup del wallet.",
            "Write the 12 words ON PAPER, in order. They are the only backup of the wallet.",
            "Escribe las 12 palabras EN PAPEL, en orden. Son la única copia de seguridad del wallet.",
            "Écrivez les 12 mots SUR PAPIER, dans l'ordre. C'est la seule sauvegarde du wallet.",
            "Escreva as 12 palavras NO PAPEL, em ordem. São o único backup da carteira.",
            "Schreiben Sie die 12 Wörter AUF PAPIER, in der richtigen Reihenfolge. Sie sind die einzige Sicherung des Wallets."],
        ["msg.seed.retype"]       = [
            "Reinserisci le 12 parole per confermare di averle scritte.",
            "Re-enter the 12 words to confirm you wrote them down.",
            "Reingresa las 12 palabras para confirmar que las has anotado.",
            "Ressaisissez les 12 mots pour confirmer que vous les avez notés.",
            "Reinsira as 12 palavras para confirmar que as anotou.",
            "Geben Sie die 12 Wörter erneut ein, um zu bestätigen, dass Sie sie notiert haben."],
        ["msg.seed.mismatch"]     = [
            "Le parole non corrispondono: ricontrolla quello che hai scritto su carta.",
            "The words do not match: check what you wrote on paper.",
            "Las palabras no coinciden: revisa lo que escribiste en papel.",
            "Les mots ne correspondent pas : vérifiez ce que vous avez écrit sur papier.",
            "As palavras não correspondem: verifique o que escreveu no papel.",
            "Die Wörter stimmen nicht überein: überprüfen Sie, was Sie auf Papier geschrieben haben."],
        ["msg.words.enter"]       = [
            "Inserisci la mnemonica BIP39 (12 o 24 parole separate da spazi).",
            "Enter the BIP39 mnemonic (12 or 24 words separated by spaces).",
            "Ingresa el mnemónico BIP39 (12 o 24 palabras separadas por espacios).",
            "Entrez le mnémonique BIP39 (12 ou 24 mots séparés par des espaces).",
            "Insira o mnemônico BIP39 (12 ou 24 palavras separadas por espaços).",
            "Geben Sie die BIP39-Mnemonic ein (12 oder 24 durch Leerzeichen getrennte Wörter)."],
        ["msg.words.invalid"]     = [
            "Mnemonica non valida (parole o checksum errati): ricontrolla.",
            "Invalid mnemonic (wrong words or checksum): check again.",
            "Mnemónico no válido (palabras o checksum incorrectos): verifica de nuevo.",
            "Mnémonique invalide (mots ou checksum incorrects) : vérifiez à nouveau.",
            "Mnemônico inválido (palavras ou checksum incorretos): verifique novamente.",
            "Ungültige Mnemonic (falsche Wörter oder Prüfsumme): bitte erneut prüfen."],
        ["msg.passphrase.info"]   = [
            "Passphrase BIP39 opzionale: cambia completamente il wallet. Se la usi, annotala A PARTE dal seed; se la perdi i fondi sono irrecuperabili. Lascia vuoto per non usarla.",
            "Optional BIP39 passphrase: it derives a completely different wallet. If you use it, note it SEPARATELY from the seed; if lost, funds are unrecoverable. Leave empty to skip.",
            "Frase de contraseña BIP39 opcional: deriva un wallet completamente diferente. Si la usas, anótala SEPARADA de la semilla; si la pierdes, los fondos son irrecuperables. Deja vacío para omitir.",
            "Phrase de passe BIP39 optionnelle : elle dérive un wallet complètement différent. Si vous l'utilisez, notez-la SÉPARÉMENT de la graine ; si vous la perdez, les fonds sont irrécupérables. Laisser vide pour ignorer.",
            "Frase-senha BIP39 opcional: deriva uma carteira completamente diferente. Se a usar, anote-a SEPARADAMENTE da semente; se a perder, os fundos são irrecuperáveis. Deixe vazio para ignorar.",
            "Optionale BIP39-Passphrase: leitet ein völlig anderes Wallet ab. Falls verwendet, GETRENNT vom Seed notieren; falls verloren, sind die Gelder unwiederbringlich. Leer lassen zum Überspringen."],
        ["msg.password.info"]     = [
            "Password di cifratura del file wallet su disco (consigliata). Non sostituisce il seed: serve solo a proteggere il file.",
            "Encryption password for the wallet file on disk (recommended). It does not replace the seed: it only protects the file.",
            "Contraseña de cifrado para el archivo wallet en disco (recomendada). No reemplaza la semilla: solo protege el archivo.",
            "Mot de passe de chiffrement pour le fichier wallet sur disque (recommandé). Ne remplace pas la graine : protège uniquement le fichier.",
            "Senha de criptografia para o arquivo da carteira no disco (recomendada). Não substitui a semente: apenas protege o arquivo.",
            "Verschlüsselungspasswort für die Wallet-Datei auf der Festplatte (empfohlen). Ersetzt nicht den Seed: schützt nur die Datei."],
        ["msg.wrongpassword"]     = ["Password errata.",                                                  "Wrong password.",              "Contraseña incorrecta.",               "Mot de passe incorrect.",                  "Senha incorreta.",                         "Falsches Passwort."],
        ["msg.opened"]            = ["Wallet aperto: connessione al server…",                             "Wallet opened: connecting to server…", "Wallet abierto: conectando al servidor…", "Wallet ouvert : connexion au serveur…",  "Carteira aberta: conectando ao servidor…", "Wallet geöffnet: Verbindung zum Server…"],
        ["msg.synced"]            = ["Sincronizzato",                                                     "Synchronized",                 "Sincronizado",                         "Synchronisé",                              "Sincronizado",                             "Synchronisiert"],
        ["msg.synced.detail"]     = [
            "transazioni verificate SPV. Aggiornamento in tempo reale attivo.",
            "SPV-verified transactions. Real-time updates active.",
            "transacciones verificadas SPV. Actualizaciones en tiempo real activas.",
            "transactions vérifiées SPV. Mises à jour en temps réel actives.",
            "transações verificadas SPV. Atualizações em tempo real ativas.",
            "SPV-verifizierte Transaktionen. Echtzeit-Updates aktiv."],
        ["msg.height"]            = ["altezza",                                                           "height",                       "altura",                               "hauteur",                                  "altura",                                   "Höhe"],
        ["msg.pending"]           = ["in attesa di conferma",                                             "pending confirmation",         "pendiente de confirmación",            "en attente de confirmation",               "aguardando confirmação",                   "ausstehende Bestätigung"],
        ["msg.notspendable"]      = ["non ancora spendibile",                                             "not yet spendable",            "aún no gastable",                      "pas encore dépensable",                    "ainda não gastável",                       "noch nicht verwendbar"],
        ["msg.settings.saved"]    = ["Impostazioni salvate.",                                             "Settings saved.",              "Configuración guardada.",              "Paramètres enregistrés.",                  "Configurações salvas.",                    "Einstellungen gespeichert."],
        ["msg.certreset"]         = [
            "Certificati SSL azzerati: riprova la connessione.",
            "SSL certificates cleared: retry the connection.",
            "Certificados SSL restablecidos: reintenta la conexión.",
            "Certificats SSL réinitialisés : réessayez la connexion.",
            "Certificados SSL redefinidos: tente novamente a conexão.",
            "SSL-Zertifikate zurückgesetzt: Verbindung erneut versuchen."],
        ["msg.error"]             = ["Errore",                                                            "Error",                        "Error",                                "Erreur",                                   "Erro",                                     "Fehler"],

        // Contatti
        ["contacts.name"]         = ["Nome",                                                              "Name",                         "Nombre",                               "Nom",                                      "Nome",                                     "Name"],
        ["contacts.address"]      = ["Indirizzo",                                                         "Address",                      "Dirección",                            "Adresse",                                  "Endereço",                                 "Adresse"],
        ["contacts.name.ph"]      = ["Nome contatto",                                                     "Contact name",                 "Nombre del contacto",                  "Nom du contact",                           "Nome do contato",                          "Kontaktname"],
        ["contacts.address.ph"]   = ["Indirizzo blockchain",                                              "Blockchain address",           "Dirección blockchain",                 "Adresse blockchain",                       "Endereço blockchain",                      "Blockchain-Adresse"],
        ["contacts.add"]          = ["Aggiungi",                                                          "Add",                          "Agregar",                              "Ajouter",                                  "Adicionar",                                "Hinzufügen"],
        ["contacts.remove"]       = ["Rimuovi selezionato",                                               "Remove selected",              "Eliminar seleccionado",                "Supprimer la sélection",                   "Remover selecionado",                      "Auswahl entfernen"],
        ["contacts.empty"]        = ["Nessun contatto salvato.",                                          "No saved contacts.",           "No hay contactos guardados.",          "Aucun contact enregistré.",                "Nenhum contato salvo.",                    "Keine gespeicherten Kontakte."],

        // Finestra impostazioni
        ["settings.title"]        = ["Impostazioni",                                                      "Settings",                     "Configuración",                        "Paramètres",                               "Configurações",                            "Einstellungen"],
        ["settings.language"]     = ["Lingua",                                                            "Language",                     "Idioma",                               "Langue",                                   "Idioma",                                   "Sprache"],
        ["settings.unit"]         = ["Unità degli importi",                                               "Amount unit",                  "Unidad de importes",                   "Unité des montants",                       "Unidade dos valores",                      "Betrageinheit"],
        ["settings.ok"]           = ["Salva",                                                             "Save",                         "Guardar",                              "Enregistrer",                              "Salvar",                                   "Speichern"],
        ["settings.cancel"]       = ["Annulla",                                                           "Cancel",                       "Cancelar",                             "Annuler",                                  "Cancelar",                                 "Abbrechen"],
        ["settings.server"]       = ["Server di indicizzazione…",                                         "Indexing server…",             "Servidor de indexación…",              "Serveur d'indexation…",                    "Servidor de indexação…",                   "Indexierungsserver…"],

        // Finestra server
        ["server.title"]          = ["Server di indicizzazione",                                          "Indexing server",              "Servidor de indexación",               "Serveur d'indexation",                     "Servidor de indexação",                    "Indexierungsserver"],
        ["server.host"]           = ["Host",                                                              "Host",                         "Host",                                 "Hôte",                                     "Host",                                     "Host"],
        ["server.port"]           = ["Porta",                                                             "Port",                         "Puerto",                               "Port",                                     "Porta",                                    "Port"],
        ["server.known"]          = ["Server conosciuti (clicca per usarlo):",                            "Known servers (click to use):", "Servidores conocidos (clic para usar):", "Serveurs connus (cliquez pour utiliser) :", "Servidores conhecidos (clique para usar):", "Bekannte Server (zum Verwenden anklicken):"],
        ["server.empty"]          = ["Nessun server conosciuto. Usa «Cerca altri server» dopo esserti connesso.", "No known servers. Use “Discover servers” after connecting.", "No hay servidores conocidos. Usa «Buscar otros servidores» tras conectar.", "Aucun serveur connu. Utilisez « Rechercher des serveurs » après connexion.", "Nenhum servidor conhecido. Use «Procurar servidores» após conectar.", "Keine bekannten Server. Nutzen Sie „Server suchen“ nach dem Verbinden."],
    };
}
