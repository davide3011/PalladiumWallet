using System.Collections.Generic;

namespace PalladiumWallet.App.Localization;

/// <summary>
/// UI localisation: key → per-language translations dictionary, with an
/// indexer bindable from XAML ({Binding Loc[key]}). On language change the
/// ViewModel replaces the instance so Avalonia re-evaluates all bindings.
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
    /// Creates a new instance for the specified language and updates the singleton
    /// used by <see cref="Tr"/>. The ViewModel assigns this instance to its Loc
    /// property so Avalonia sees a different reference and re-evaluates all
    /// {Binding Loc[key]} bindings.
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
["menu.file.close"]       = ["Chiudi wallet",                                                     "Close wallet",                 "Cerrar wallet",                        "Fermer le wallet",                         "Fechar carteira",                          "Wallet schließen"],
        ["menu.file.quit"]        = ["Esci",                                                               "Quit",                         "Salir",                                "Quitter",                                  "Sair",                                     "Beenden"],
        ["menu.net"]              = ["_Rete",                                                             "_Network",                     "_Red",                                 "_Réseau",                                  "_Rede",                                    "_Netzwerk"],
        ["menu.net.discover"]     = ["Cerca altri server (peer)",                                         "Discover servers (peers)",     "Buscar otros servidores (peers)",      "Rechercher d'autres serveurs (pairs)",     "Procurar outros servidores (peers)",        "Weitere Server suchen (Peers)"],
        ["menu.net.resetcerts"]   = ["Reset certificati SSL",                                             "Reset SSL certificates",       "Restablecer certificados SSL",         "Réinitialiser les certificats SSL",        "Redefinir certificados SSL",               "SSL-Zertifikate zurücksetzen"],
        ["menu.settings"]         = ["_Impostazioni",                                                     "_Settings",                    "_Configuración",                       "_Paramètres",                              "_Configurações",                           "_Einstellungen"],
        ["menu.help"]             = ["_Help",                                                             "_Help",                        "_Ayuda",                               "_Aide",                                    "_Ajuda",                                   "_Hilfe"],
        ["help.title"]            = ["Informazioni",                                                     "About",                        "Información",                          "À propos",                                 "Sobre",                                    "Über"],
        ["help.info"]             = [
            "Wallet SPV per la criptovaluta Palladium (PLM).",
            "SPV wallet for the Palladium (PLM) cryptocurrency.",
            "Monedero SPV para la criptomoneda Palladium (PLM).",
            "Portefeuille SPV pour la cryptomonnaie Palladium (PLM).",
            "Carteira SPV para a criptomoeda Palladium (PLM).",
            "SPV-Wallet für die Kryptowährung Palladium (PLM)."],
        ["help.tab.info"]         = ["Info",                                                               "Info",                         "Info",                                 "Info",                                     "Info",                                     "Info"],
        ["help.tab.donate"]       = ["Dona",                                                               "Donate",                       "Donar",                                "Faire un don",                             "Doar",                                     "Spenden"],
        ["donate.desc"]           = [
            "Se questo wallet ti è utile, considera una piccola donazione allo sviluppatore.",
            "If you find this wallet useful, consider a small donation to the developer.",
            "Si esta wallet te resulta útil, considera una pequeña donación al desarrollador.",
            "Si ce portefeuille vous est utile, envisagez un petit don au développeur.",
            "Se esta carteira é útil para você, considere uma pequena doação ao desenvolvedor.",
            "Wenn Ihnen dieses Wallet nützlich ist, erwägen Sie eine kleine Spende an den Entwickler."],
        ["donate.dev.address"]    = ["Indirizzo sviluppatore",                                             "Developer address",            "Dirección del desarrollador",          "Adresse du développeur",                   "Endereço do desenvolvedor",                "Entwickleradresse"],
        ["donate.amount"]         = ["Importo donazione",                                                  "Donation amount",              "Monto de donación",                    "Montant du don",                           "Valor da doação",                          "Spendenbetrag"],
        ["donate.prepare"]        = ["Prepara donazione",                                                  "Prepare donation",             "Preparar donación",                    "Préparer le don",                          "Preparar doação",                          "Spende vorbereiten"],
        ["donate.confirm"]        = ["Conferma e invia",                                                   "Confirm and send",             "Confirmar y enviar",                   "Confirmer et envoyer",                     "Confirmar e enviar",                       "Bestätigen und senden"],
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
        ["wiz.choose.title"]      = ["Scegli il wallet da aprire",                                        "Choose the wallet to open",    "Elige el wallet a abrir",              "Choisissez le wallet à ouvrir",            "Escolha a carteira a abrir",               "Wallet zum Öffnen wählen"],
["wiz.open.btn"]          = ["Apri il wallet esistente",                                          "Open existing wallet",         "Abrir wallet existente",               "Ouvrir le wallet existant",                "Abrir carteira existente",                 "Vorhandenes Wallet öffnen"],
        ["wiz.new.btn"]           = ["Crea un nuovo wallet",                                              "Create a new wallet",          "Crear nuevo wallet",                   "Créer un nouveau wallet",                  "Criar nova carteira",                      "Neues Wallet erstellen"],
        ["wiz.restore.btn"]       = ["Ripristina da seed",                                                "Restore from seed",            "Restaurar desde semilla",              "Restaurer depuis la graine",               "Restaurar da semente",                     "Aus Seed wiederherstellen"],
        ["wiz.importxkey.btn"]    = ["Importa xpub / xprv",                                               "Import xpub / xprv",           "Importar xpub / xprv",                 "Importer xpub / xprv",                     "Importar xpub / xprv",                     "xpub / xprv importieren"],
        ["wiz.importwif.btn"]     = ["Importa chiave WIF",                                                 "Import WIF key",               "Importar clave WIF",                   "Importer clé WIF",                         "Importar chave WIF",                       "WIF-Schlüssel importieren"],
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
        ["wiz.name.label"]        = ["Nome wallet (opzionale)",                                           "Wallet name (optional)",       "Nombre del wallet (opcional)",         "Nom du wallet (optionnel)",                "Nome da carteira (opcional)",              "Wallet-Name (optional)"],
        ["wiz.name.placeholder"]  = ["es. risparmio, trading… (lascia vuoto per nome automatico)",        "e.g. savings, trading… (leave blank for auto name)", "p.ej. ahorro, trading… (deja en blanco para nombre automático)", "ex. épargne, trading… (laisser vide pour nom automatique)", "ex. poupança, trading… (deixe em branco para nome automático)", "z.B. Sparen, Trading… (leer lassen für automatischen Namen)"],
        ["msg.wallet.exists"]     = ["Esiste già un wallet con questo nome. Scegli un nome diverso.",      "A wallet with this name already exists. Choose a different name.", "Ya existe un wallet con este nombre. Elige un nombre diferente.", "Un wallet avec ce nom existe déjà. Choisissez un nom différent.", "Já existe uma carteira com este nome. Escolha um nome diferente.", "Ein Wallet mit diesem Namen existiert bereits. Wähle einen anderen Namen."],
        ["wiz.password.title"]    = ["Password del file wallet",                                          "Wallet file password",         "Contraseña del archivo wallet",        "Mot de passe du fichier wallet",           "Senha do arquivo da carteira",             "Wallet-Dateipasswort"],
        ["wiz.password.placeholder"] = ["Consigliata (vuoto = file in chiaro su disco)",                 "Recommended (empty = plaintext file on disk)", "Recomendada (vacío = archivo en texto claro en disco)", "Recommandé (vide = fichier en texte clair sur disque)", "Recomendada (vazio = arquivo em texto simples no disco)", "Empfohlen (leer = Klartextdatei auf Disk)"],
        ["wiz.password.create"]   = ["Crea il wallet",                                                    "Create wallet",                "Crear wallet",                         "Créer le wallet",                          "Criar carteira",                           "Wallet erstellen"],
        ["wiz.password.confirm"]  = ["Ripeti la password",                                                "Repeat the password",          "Repite la contraseña",                 "Répétez le mot de passe",                  "Repita a senha",                           "Passwort wiederholen"],
        ["wiz.password.encrypt"]  = ["Cifra il file wallet con la password",                              "Encrypt the wallet file with the password", "Cifrar el archivo wallet con la contraseña", "Chiffrer le fichier wallet avec le mot de passe", "Criptografar o arquivo da carteira com a senha", "Wallet-Datei mit dem Passwort verschlüsseln"],
        ["wiz.password.encrypt.hint"] = [
            "Attenzione: senza cifratura il seed resta in chiaro sul disco.",
            "Warning: without encryption the seed stays in plaintext on disk.",
            "Atención: sin cifrado la semilla queda en texto claro en el disco.",
            "Attention : sans chiffrement, la graine reste en clair sur le disque.",
            "Atenção: sem criptografia a semente fica em texto simples no disco.",
            "Achtung: ohne Verschlüsselung bleibt der Seed im Klartext auf der Festplatte."],
        ["wiz.back"]              = ["Indietro",                                                          "Back",                         "Atrás",                                "Retour",                                   "Voltar",                                   "Zurück"],
        ["wiz.next"]              = ["Avanti",                                                            "Next",                         "Siguiente",                            "Suivant",                                  "Próximo",                                  "Weiter"],
        ["wiz.scripttype.title"]  = ["Tipo di script e indirizzi",                                       "Script type and addresses",    "Tipo de script y direcciones",         "Type de script et adresses",               "Tipo de script e endereços",               "Skripttyp und Adressen"],
        ["wiz.scripttype.hint"]   = [
            "Determina il formato degli indirizzi. Se non sai cosa scegliere, usa Native SegWit.",
            "Determines the address format. If unsure, use Native SegWit.",
            "Determina el formato de los direcciones. Si no sabes, usa Native SegWit.",
            "Détermine le format des adresses. En cas de doute, utilisez Native SegWit.",
            "Determina o formato dos endereços. Em caso de dúvida, use Native SegWit.",
            "Bestimmt das Adressformat. Wenn Sie unsicher sind, verwenden Sie Native SegWit."],
        ["wiz.scripttype.legacy.desc"]  = ["BIP44 · m/44'/… · indirizzi P",  "BIP44 · m/44'/… · P addresses",  "BIP44 · m/44'/… · direcciones P",  "BIP44 · m/44'/… · adresses P",  "BIP44 · m/44'/… · endereços P",  "BIP44 · m/44'/… · P-Adressen"],
        ["wiz.scripttype.wrapped.desc"] = ["BIP49 · m/49'/… · indirizzi 3",  "BIP49 · m/49'/… · 3 addresses",  "BIP49 · m/49'/… · direcciones 3",  "BIP49 · m/49'/… · adresses 3",  "BIP49 · m/49'/… · endereços 3",  "BIP49 · m/49'/… · 3-Adressen"],
        ["wiz.scripttype.native.desc"]  = ["BIP84 · m/84'/… · indirizzi plm1q — consigliato", "BIP84 · m/84'/… · plm1q addresses — recommended", "BIP84 · m/84'/… · direcciones plm1q — recomendado", "BIP84 · m/84'/… · adresses plm1q — recommandé", "BIP84 · m/84'/… · endereços plm1q — recomendado", "BIP84 · m/84'/… · plm1q-Adressen — empfohlen"],
        ["wiz.scripttype.taproot.desc"] = ["BIP86 · m/86'/… · indirizzi plm1p", "BIP86 · m/86'/… · plm1p addresses", "BIP86 · m/86'/… · direcciones plm1p", "BIP86 · m/86'/… · adresses plm1p", "BIP86 · m/86'/… · endereços plm1p", "BIP86 · m/86'/… · plm1p-Adressen"],
        ["wiz.importxkey.title"]  = ["Importa chiave estesa",                                              "Import extended key",          "Importar clave extendida",             "Importer la clé étendue",                  "Importar chave estendida",                 "Erweiterten Schlüssel importieren"],
        ["wiz.importxkey.hint"]   = [
            "Incolla una xpub/zpub/ypub (watch-only) o xprv/zprv/yprv (spendibile). Il tipo di script viene rilevato automaticamente.",
            "Paste an xpub/zpub/ypub (watch-only) or xprv/zprv/yprv (spendable). The script type is detected automatically.",
            "Pega un xpub/zpub/ypub (solo lectura) o xprv/zprv/yprv (gastable). El tipo de script se detecta automáticamente.",
            "Collez un xpub/zpub/ypub (lecture seule) ou xprv/zprv/yprv (dépensable). Le type de script est détecté automatiquement.",
            "Cole um xpub/zpub/ypub (somente leitura) ou xprv/zprv/yprv (gastável). O tipo de script é detectado automaticamente.",
            "Fügen Sie einen xpub/zpub/ypub (nur lesend) oder xprv/zprv/yprv (ausgabefähig) ein. Der Skripttyp wird automatisch erkannt."],
        ["wiz.importxkey.placeholder"] = ["xpub… / zpub… / ypub… / xprv… / zprv…",                       "xpub… / zpub… / ypub… / xprv… / zprv…", "xpub… / zpub… / ypub… / xprv… / zprv…", "xpub… / zpub… / ypub… / xprv… / zprv…", "xpub… / zpub… / ypub… / xprv… / zprv…", "xpub… / zpub… / ypub… / xprv… / zprv…"],
        ["wiz.importwif.title"]   = ["Importa chiave privata WIF",                                         "Import WIF private key",       "Importar clave privada WIF",           "Importer la clé privée WIF",               "Importar chave privada WIF",               "WIF-Privatschlüssel importieren"],
        ["wiz.importwif.hint"]    = [
            "Incolla una o più chiavi WIF (una per riga). Puoi importare più chiavi per controllare più indirizzi con lo stesso wallet.",
            "Paste one or more WIF keys (one per line). You can import multiple keys to control multiple addresses with the same wallet.",
            "Pega una o más claves WIF (una por línea). Puedes importar múltiples claves para controlar múltiples direcciones con el mismo wallet.",
            "Collez une ou plusieurs clés WIF (une par ligne). Vous pouvez importer plusieurs clés pour contrôler plusieurs adresses avec le même wallet.",
            "Cole uma ou mais chaves WIF (uma por linha). Você pode importar várias chaves para controlar vários endereços com a mesma carteira.",
            "Fügen Sie einen oder mehrere WIF-Schlüssel ein (einer pro Zeile). Sie können mehrere Schlüssel importieren, um mehrere Adressen mit demselben Wallet zu verwalten."],
        ["wiz.importwif.placeholder"] = ["K… / L… / 5… (una chiave per riga)",                            "K… / L… / 5… (one key per line)", "K… / L… / 5… (una clave por línea)", "K… / L… / 5… (une clé par ligne)", "K… / L… / 5… (uma chave por linha)", "K… / L… / 5… (ein Schlüssel pro Zeile)"],

        // Import error messages
        ["msg.xkey.required"]     = ["Incolla una chiave estesa (xpub/xprv o variante).",                  "Paste an extended key (xpub/xprv or variant).", "Pega una clave extendida (xpub/xprv o variante).", "Collez une clé étendue (xpub/xprv ou variante).", "Cole uma chave estendida (xpub/xprv ou variante).", "Fügen Sie einen erweiterten Schlüssel ein (xpub/xprv oder Variante)."],
        ["msg.xkey.invalid"]      = ["Chiave estesa non riconosciuta per questa rete.",                     "Extended key not recognised for this network.", "Clave extendida no reconocida para esta red.", "Clé étendue non reconnue pour ce réseau.", "Chave estendida não reconhecida para esta rede.", "Erweiterter Schlüssel für dieses Netzwerk nicht erkannt."],
        ["msg.wif.required"]      = ["Incolla almeno una chiave WIF.",                                      "Paste at least one WIF key.",  "Pega al menos una clave WIF.",         "Collez au moins une clé WIF.",             "Cole pelo menos uma chave WIF.",           "Fügen Sie mindestens einen WIF-Schlüssel ein."],
        ["msg.wif.invalid"]       = ["Chiave WIF non valida per questa rete.",                              "WIF key not valid for this network.", "Clave WIF no válida para esta red.", "Clé WIF invalide pour ce réseau.",        "Chave WIF inválida para esta rede.",       "WIF-Schlüssel für dieses Netzwerk ungültig."],

        // Wallet panel
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
        ["receive.copy"]          = ["Copia",                                                             "Copy",                         "Copiar",                               "Copier",                                   "Copiar",                                   "Kopieren"],
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
        ["addr.privkey.prompt.title"] = ["Conferma identità",                                           "Confirm identity",             "Confirmar identidad",                  "Confirmer l'identité",                     "Confirmar identidade",                     "Identität bestätigen"],
        ["addr.privkey.prompt.desc"]  = ["Inserisci la password del wallet per visualizzare la chiave privata.", "Enter the wallet password to reveal the private key.", "Ingresa la contraseña del wallet para ver la clave privada.", "Entrez le mot de passe du wallet pour afficher la clé privée.", "Digite a senha da carteira para ver a chave privada.", "Geben Sie das Wallet-Passwort ein, um den privaten Schlüssel anzuzeigen."],
        ["addr.hide.privkey"]     = ["Nascondi",                                                          "Hide",                         "Ocultar",                              "Masquer",                                  "Ocultar",                                  "Ausblenden"],
        ["addr.receive"]          = ["ricezione",                                                         "receive",                      "recepción",                            "réception",                                "recebimento",                              "Empfang"],
        ["addr.change"]           = ["change",                                                            "change",                       "cambio",                               "monnaie",                                  "troco",                                    "Wechselgeld"],
        ["addr.info.title"]       = ["Informazioni indirizzo",                                          "Address information",          "Información de dirección",             "Informations sur l'adresse",               "Informações do endereço",                  "Adressinformationen"],
        ["addr.close"]            = ["Chiudi",                                                          "Close",                        "Cerrar",                               "Fermer",                                   "Fechar",                                   "Schließen"],

        // History → transaction detail
        ["history.hint"]          = ["Doppio click su una transazione per i dettagli.",                  "Double-click a transaction for details.", "Doble clic en una transacción para ver los detalles.", "Double-cliquez sur une transaction pour les détails.", "Clique duplo numa transação para ver os detalhes.", "Doppelklick auf eine Transaktion für Details."],
        ["tx.title"]              = ["Dettagli transazione",                                            "Transaction details",          "Detalles de la transacción",           "Détails de la transaction",                "Detalhes da transação",                    "Transaktionsdetails"],
        ["tx.close"]              = ["Chiudi",                                                          "Close",                        "Cerrar",                               "Fermer",                                   "Fechar",                                   "Schließen"],
        ["tx.loading"]            = ["Carico i dati della transazione dal server…",                     "Loading transaction data from the server…", "Cargando los datos de la transacción desde el servidor…", "Chargement des données de la transaction depuis le serveur…", "Carregando os dados da transação do servidor…", "Lade Transaktionsdaten vom Server…"],
        ["tx.status"]             = ["Stato",                                                           "Status",                       "Estado",                               "Statut",                                   "Estado",                                   "Status"],
        ["tx.status.mempool"]     = ["0 conferme · in mempool",                                         "0 confirmations · in mempool", "0 confirmaciones · en mempool",        "0 confirmation · dans le mempool",         "0 confirmações · no mempool",              "0 Bestätigungen · im Mempool"],
        ["tx.status.confirmations"] = ["conferme",                                                      "confirmations",                "confirmaciones",                       "confirmations",                            "confirmações",                             "Bestätigungen"],
        ["tx.status.block"]       = ["blocco",                                                          "block",                        "bloque",                               "bloc",                                     "bloco",                                    "Block"],
        ["tx.mempool"]            = ["in mempool",                                                      "in mempool",                   "en mempool",                           "dans le mempool",                          "no mempool",                               "im Mempool"],
        ["tx.date"]               = ["Data",                                                            "Date",                         "Fecha",                                "Date",                                     "Data",                                     "Datum"],
        ["tx.to"]                 = ["A",                                                               "To",                           "Para",                                 "À",                                        "Para",                                     "An"],
        ["tx.from"]               = ["Da",                                                              "From",                         "De",                                   "De",                                       "De",                                       "Von"],
        ["tx.debit"]              = ["Debito",                                                          "Debit",                        "Débito",                               "Débit",                                    "Débito",                                   "Soll"],
        ["tx.credit"]             = ["Credito",                                                         "Credit",                       "Crédito",                              "Crédit",                                   "Crédito",                                  "Haben"],
        ["tx.fee"]                = ["Fee transazione",                                                 "Transaction fee",              "Comisión de transacción",              "Frais de transaction",                     "Taxa da transação",                        "Transaktionsgebühr"],
        ["tx.feerate"]            = ["Fee per vByte",                                                   "Fee per vByte",                "Comisión por vByte",                   "Frais par vOctet",                         "Taxa por vByte",                           "Gebühr pro vByte"],
        ["tx.net"]                = ["Importo netto",                                                   "Net amount",                   "Importe neto",                         "Montant net",                              "Valor líquido",                            "Nettobetrag"],
        ["tx.id"]                 = ["ID transazione",                                                  "Transaction ID",               "ID de transacción",                    "ID de transaction",                        "ID da transação",                          "Transaktions-ID"],
        ["tx.size.total"]         = ["Dimensione totale",                                              "Total size",                   "Tamaño total",                         "Taille totale",                            "Tamanho total",                            "Gesamtgröße"],
        ["tx.size.virtual"]       = ["Dimensione virtuale",                                            "Virtual size",                 "Tamaño virtual",                       "Taille virtuelle",                         "Tamanho virtual",                          "Virtuelle Größe"],
        ["tx.rbf"]                = ["Sostituibile (RBF)",                                              "Replaceable (RBF)",            "Reemplazable (RBF)",                   "Remplaçable (RBF)",                        "Substituível (RBF)",                       "Ersetzbar (RBF)"],
["tx.inputs"]             = ["Input",                                                           "Inputs",                       "Entradas",                             "Entrées",                                  "Entradas",                                 "Eingänge"],
        ["tx.outputs"]            = ["Output",                                                          "Outputs",                      "Salidas",                              "Sorties",                                  "Saídas",                                   "Ausgänge"],
        ["tx.yes"]                = ["Sì",                                                              "Yes",                          "Sí",                                   "Oui",                                      "Sim",                                      "Ja"],
        ["tx.no"]                 = ["No",                                                              "No",                           "No",                                   "Non",                                      "Não",                                      "Nein"],
        ["tx.needconnection"]     = ["Connettiti al server per vedere i dettagli della transazione.",   "Connect to the server to view transaction details.", "Conéctate al servidor para ver los detalles de la transacción.", "Connectez-vous au serveur pour voir les détails de la transaction.", "Conecte-se ao servidor para ver os detalhes da transação.", "Mit dem Server verbinden, um die Transaktionsdetails zu sehen."],
        ["tx.sect.overview"]      = ["Panoramica",                                                       "Overview",                     "Resumen",                              "Aperçu",                                   "Visão geral",                              "Übersicht"],
        ["tx.sect.amounts"]       = ["Importi e commissioni",                                            "Amounts & fees",               "Importes y comisiones",                "Montants et frais",                        "Valores e taxas",                          "Beträge & Gebühren"],
        ["tx.sect.tech"]          = ["Dettagli tecnici",                                                 "Technical details",            "Detalles técnicos",                    "Détails techniques",                       "Detalhes técnicos",                        "Technische Details"],
        ["tx.coinbase"]           = ["Coinbase",                                                        "Coinbase",                     "Coinbase",                             "Coinbase",                                 "Coinbase",                                 "Coinbase"],
        ["tx.coinbase.newcoins"]  = ["Nuova emissione (mining)",                                         "Newly generated (mining)",     "Nueva emisión (minería)",              "Nouvelle émission (minage)",               "Nova emissão (mineração)",                 "Neu erzeugt (Mining)"],
        ["send.from.contact"]     = ["Da contatti:",                                                      "From contacts:",               "De contactos:",                        "Depuis les contacts :",                    "De contatos:",                             "Aus Kontakten:"],
        ["send.contact.hint"]     = ["seleziona per riempire l'indirizzo",                                "select to fill address",       "selecciona para rellenar la dirección", "sélectionner pour remplir l'adresse",      "selecione para preencher o endereço",      "auswählen um Adresse einzufügen"],
        ["send.to"]               = ["Indirizzo destinatario",                                            "Recipient address",            "Dirección destinataria",               "Adresse du destinataire",                  "Endereço do destinatário",                 "Empfängeradresse"],
        ["send.amount"]           = ["Importo",                                                           "Amount",                       "Importe",                              "Montant",                                  "Valor",                                    "Betrag"],
        ["send.all"]              = ["Invia tutto",                                                       "Send all",                     "Enviar todo",                          "Tout envoyer",                             "Enviar tudo",                              "Alles senden"],
        ["send.feerate"]          = ["fee sat/vB:",                                                       "fee sat/vB:",                  "tarifa sat/vB:",                       "frais sat/vB :",                           "taxa sat/vB:",                             "Gebühr sat/vB:"],
        ["send.prepare"]          = ["Prepara transazione",                                               "Prepare transaction",          "Preparar transacción",                 "Préparer la transaction",                  "Preparar transação",                       "Transaktion vorbereiten"],
        ["send.scan"]             = ["Scansiona QR",                                                      "Scan QR",                      "Escanear QR",                          "Scanner QR",                               "Escanear QR",                              "QR scannen"],
        ["send.confirm"]          = ["CONFERMA E TRASMETTI",                                              "CONFIRM AND BROADCAST",        "CONFIRMAR Y TRANSMITIR",               "CONFIRMER ET DIFFUSER",                    "CONFIRMAR E TRANSMITIR",                   "BESTÄTIGEN UND SENDEN"],
        ["send.sect.recipient"]   = ["Destinatario",                                                       "Recipient",                    "Destinatario",                         "Destinataire",                             "Destinatário",                             "Empfänger"],
        ["send.sect.amount"]      = ["Importo e commissione",                                              "Amount & fee",                 "Importe y comisión",                   "Montant et frais",                         "Valor e taxa",                             "Betrag & Gebühr"],
        ["send.summary"]          = ["Riepilogo",                                                          "Summary",                      "Resumen",                              "Résumé",                                   "Resumo",                                   "Zusammenfassung"],
        ["receive.your.address"]  = ["Il tuo indirizzo",                                                   "Your address",                 "Tu dirección",                         "Votre adresse",                            "Seu endereço",                             "Deine Adresse"],

        // Wallet info overlay
        ["menu.wallet"]              = ["_Wallet",                                                          "_Wallet",                      "_Wallet",                              "_Wallet",                                  "_Wallet",                                  "_Wallet"],
        ["walletinfo.title"]         = ["Informazioni Wallet",                                              "Wallet Information",           "Información del Wallet",               "Informations Wallet",                      "Informações da Carteira",                  "Wallet-Informationen"],
        ["walletinfo.file"]          = ["File",                                                             "File",                         "Archivo",                              "Fichier",                                  "Arquivo",                                  "Datei"],
        ["walletinfo.network"]       = ["Rete",                                                             "Network",                      "Red",                                  "Réseau",                                   "Rede",                                     "Netzwerk"],
        ["walletinfo.type"]          = ["Tipo wallet",                                                      "Wallet type",                  "Tipo de wallet",                       "Type de wallet",                           "Tipo de carteira",                         "Wallet-Typ"],
        ["walletinfo.type.seed"]     = ["HD (seed BIP39)",                                                  "HD (BIP39 seed)",              "HD (seed BIP39)",                      "HD (graine BIP39)",                        "HD (semente BIP39)",                       "HD (BIP39-Seed)"],
        ["walletinfo.type.xprv"]     = ["HD (xprv importato)",                                              "HD (imported xprv)",           "HD (xprv importado)",                  "HD (xprv importé)",                        "HD (xprv importado)",                      "HD (importierter xprv)"],
        ["walletinfo.type.wif"]      = ["Chiave WIF importata",                                             "Imported WIF key",             "Clave WIF importada",                  "Clé WIF importée",                         "Chave WIF importada",                      "Importierter WIF-Schlüssel"],
        ["walletinfo.type.watchonly"] = ["Watch-only (xpub)",                                               "Watch-only (xpub)",            "Watch-only (xpub)",                    "Watch-only (xpub)",                        "Watch-only (xpub)",                        "Watch-only (xpub)"],
        ["walletinfo.script"]        = ["Script",                                                           "Script",                       "Script",                               "Script",                                   "Script",                                   "Script"],
        ["walletinfo.derivpath"]     = ["Percorso derivazione",                                             "Derivation path",              "Ruta de derivación",                   "Chemin de dérivation",                     "Caminho de derivação",                     "Ableitungspfad"],
        ["walletinfo.xpub"]          = ["Chiave pubblica estesa",                                           "Extended public key",          "Clave pública extendida",              "Clé publique étendue",                     "Chave pública estendida",                  "Erweiterter öffentlicher Schlüssel"],
        ["walletinfo.fingerprint"]   = ["Master fingerprint",                                               "Master fingerprint",           "Huella maestra",                       "Empreinte maître",                         "Impressão digital mestre",                 "Master-Fingerprint"],
        ["walletinfo.seed.section"]  = ["Seed (mnemonica BIP39)",                                           "Seed (BIP39 mnemonic)",        "Semilla (mnemónico BIP39)",            "Graine (mnémonique BIP39)",                "Semente (mnemônico BIP39)",                "Seed (BIP39-Mnemonic)"],
        ["walletinfo.seed.noseed"]   = ["Questo wallet non ha una seed (watch-only o importato).",          "This wallet has no seed (watch-only or imported).", "Este wallet no tiene seed (watch-only o importado).", "Ce wallet n'a pas de graine (watch-only ou importé).", "Esta carteira não tem semente (watch-only ou importada).", "Dieses Wallet hat keinen Seed (watch-only oder importiert)."],
        ["walletinfo.seed.password"] = ["Password del file wallet per sbloccare il seed:",                  "Wallet file password to unlock the seed:", "Contraseña del archivo para desbloquear la seed:", "Mot de passe du fichier pour déverrouiller la graine :", "Senha do arquivo para desbloquear a semente:", "Dateipasswort zum Entsperren des Seeds:"],
        ["walletinfo.seed.reveal"]   = ["Mostra seed",                                                      "Show seed",                    "Mostrar seed",                         "Afficher la graine",                       "Mostrar semente",                          "Seed anzeigen"],
        ["walletinfo.seed.hide"]     = ["Nascondi",                                                         "Hide",                         "Ocultar",                              "Masquer",                                  "Ocultar",                                  "Ausblenden"],
        ["walletinfo.seed.warning"]  = [
            "Non condividere mai queste parole. Chi le possiede controlla i fondi.",
            "Never share these words. Whoever holds them controls the funds.",
            "Nunca compartas estas palabras. Quien las tenga controla los fondos.",
            "Ne partagez jamais ces mots. Celui qui les possède contrôle les fonds.",
            "Nunca compartilhe essas palavras. Quem as tiver controla os fundos.",
            "Teilen Sie diese Wörter niemals. Wer sie hat, kontrolliert die Gelder."],
        ["walletinfo.passphrase"]    = ["Passphrase BIP39",                                                 "BIP39 passphrase",             "Frase de contraseña BIP39",            "Phrase de passe BIP39",                    "Frase-senha BIP39",                        "BIP39-Passphrase"],
        ["walletinfo.passphrase.set"] = ["(impostata)",                                                     "(set)",                        "(establecida)",                        "(définie)",                                "(definida)",                               "(gesetzt)"],

        // Connection status
        ["conn.none"]             = ["non connesso",                                                      "not connected",                "no conectado",                         "non connecté",                             "não conectado",                            "nicht verbunden"],
        ["conn.disconnected"]     = ["disconnesso",                                                       "disconnected",                 "desconectado",                         "déconnecté",                               "desconectado",                             "getrennt"],
        ["conn.reconnecting"]     = ["riconnessione…",                                                    "reconnecting…",                "reconectando…",                        "reconnexion…",                             "reconectando…",                            "Verbindung wird wiederhergestellt…"],
        ["conn.error"]            = ["errore di connessione",                                             "connection error",             "error de conexión",                    "erreur de connexion",                      "erro de conexão",                          "Verbindungsfehler"],
        ["conn.certchanged"]      = ["certificato cambiato",                                              "certificate changed",          "certificado cambiado",                 "certificat modifié",                       "certificado alterado",                     "Zertifikat geändert"],
        ["conn.connectedto"]      = ["connesso",                                                          "connected",                    "conectado",                            "connecté",                                 "conectado",                                "verbunden"],
        ["conn.connectingto"]     = ["connessione a",                                                     "connecting to",                "conectando a",                         "connexion à",                              "conectando a",                             "Verbindung zu"],

        // Main status messages
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
        ["msg.choose.wallet"]     = ["Più wallet disponibili: scegline uno.",                             "Multiple wallets available: pick one.", "Varios wallets disponibles: elige uno.", "Plusieurs wallets disponibles : choisissez-en un.", "Várias carteiras disponíveis: escolha uma.", "Mehrere Wallets verfügbar: wählen Sie eines."],
        ["msg.password.required"] = [
            "Inserisci una password per cifrare il wallet (o togli la spunta «Cifra il file wallet»).",
            "Enter a password to encrypt the wallet (or uncheck “Encrypt the wallet file”).",
            "Ingresa una contraseña para cifrar el wallet (o desmarca «Cifrar el archivo wallet»).",
            "Entrez un mot de passe pour chiffrer le wallet (ou décochez « Chiffrer le fichier wallet »).",
            "Digite uma senha para criptografar a carteira (ou desmarque «Criptografar o arquivo da carteira»).",
            "Geben Sie ein Passwort zum Verschlüsseln ein (oder deaktivieren Sie „Wallet-Datei verschlüsseln“)."],
        ["msg.password.mismatch"] = [
            "Le due password non coincidono.",
            "The two passwords do not match.",
            "Las dos contraseñas no coinciden.",
            "Les deux mots de passe ne correspondent pas.",
            "As duas senhas não coincidem.",
            "Die beiden Passwörter stimmen nicht überein."],
        ["msg.wrongpassword"]     = ["Password errata.",                                                  "Wrong password.",              "Contraseña incorrecta.",               "Mot de passe incorrect.",                  "Senha incorreta.",                         "Falsches Passwort."],
        ["msg.wallet.locked"]     = ["Wallet già aperto in un'altra istanza dell'applicazione.",          "Wallet already open in another instance of the application.", "El wallet ya está abierto en otra instancia de la aplicación.", "Le wallet est déjà ouvert dans une autre instance de l'application.", "A carteira já está aberta em outra instância do aplicativo.", "Wallet ist bereits in einer anderen Instanz der Anwendung geöffnet."],
        ["msg.wallet.noaccess"]   = ["Impossibile accedere al file del wallet: verificare i permessi.",   "Cannot access the wallet file: check file permissions.", "No se puede acceder al archivo del wallet: verifique los permisos.", "Impossible d'accéder au fichier du wallet : vérifiez les autorisations.", "Não é possível acessar o arquivo da carteira: verifique as permissões.", "Zugriff auf die Wallet-Datei nicht möglich: Berechtigungen prüfen."],
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

        // Contacts
        ["contacts.name"]         = ["Nome",                                                              "Name",                         "Nombre",                               "Nom",                                      "Nome",                                     "Name"],
        ["contacts.address"]      = ["Indirizzo",                                                         "Address",                      "Dirección",                            "Adresse",                                  "Endereço",                                 "Adresse"],
        ["contacts.name.ph"]      = ["Nome contatto",                                                     "Contact name",                 "Nombre del contacto",                  "Nom du contact",                           "Nome do contato",                          "Kontaktname"],
        ["contacts.address.ph"]   = ["Indirizzo blockchain",                                              "Blockchain address",           "Dirección blockchain",                 "Adresse blockchain",                       "Endereço blockchain",                      "Blockchain-Adresse"],
        ["contacts.add"]          = ["Aggiungi",                                                          "Add",                          "Agregar",                              "Ajouter",                                  "Adicionar",                                "Hinzufügen"],
        ["contacts.remove"]       = ["Rimuovi selezionato",                                               "Remove selected",              "Eliminar seleccionado",                "Supprimer la sélection",                   "Remover selecionado",                      "Auswahl entfernen"],
        ["contacts.empty"]        = ["Nessun contatto salvato.",                                          "No saved contacts.",           "No hay contactos guardados.",          "Aucun contact enregistré.",                "Nenhum contato salvo.",                    "Keine gespeicherten Kontakte."],

        // Settings window
        ["settings.title"]        = ["Impostazioni",                                                      "Settings",                     "Configuración",                        "Paramètres",                               "Configurações",                            "Einstellungen"],
        ["settings.language"]     = ["Lingua",                                                            "Language",                     "Idioma",                               "Langue",                                   "Idioma",                                   "Sprache"],
        ["settings.unit"]         = ["Unità degli importi",                                               "Amount unit",                  "Unidad de importes",                   "Unité des montants",                       "Unidade dos valores",                      "Betrageinheit"],
        ["settings.ok"]           = ["Salva",                                                             "Save",                         "Guardar",                              "Enregistrer",                              "Salvar",                                   "Speichern"],
        ["settings.cancel"]       = ["Annulla",                                                           "Cancel",                       "Cancelar",                             "Annuler",                                  "Cancelar",                                 "Abbrechen"],
        ["settings.server"]       = ["Server di indicizzazione…",                                         "Indexing server…",             "Servidor de indexación…",              "Serveur d'indexation…",                    "Servidor de indexação…",                   "Indexierungsserver…"],

        // Server window
        ["server.title"]          = ["Server di indicizzazione",                                          "Indexing server",              "Servidor de indexación",               "Serveur d'indexation",                     "Servidor de indexação",                    "Indexierungsserver"],
        ["server.host"]           = ["Host",                                                              "Host",                         "Host",                                 "Hôte",                                     "Host",                                     "Host"],
        ["server.port"]           = ["Porta",                                                             "Port",                         "Puerto",                               "Port",                                     "Porta",                                    "Port"],
        ["server.known"]          = ["Server conosciuti (clicca per usarlo):",                            "Known servers (click to use):", "Servidores conocidos (clic para usar):", "Serveurs connus (cliquez pour utiliser) :", "Servidores conhecidos (clique para usar):", "Bekannte Server (zum Verwenden anklicken):"],
        ["server.empty"]          = ["Nessun server conosciuto. Usa «Cerca altri server» dopo esserti connesso.", "No known servers. Use “Discover servers” after connecting.", "No hay servidores conocidos. Usa «Buscar otros servidores» tras conectar.", "Aucun serveur connu. Utilisez « Rechercher des serveurs » après connexion.", "Nenhum servidor conhecido. Use «Procurar servidores» após conectar.", "Keine bekannten Server. Nutzen Sie „Server suchen“ nach dem Verbinden."],
    };
}
