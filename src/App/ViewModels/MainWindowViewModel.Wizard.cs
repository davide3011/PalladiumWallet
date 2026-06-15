using System;
using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PalladiumWallet.App.Localization;
using PalladiumWallet.Core.Chain;
using PalladiumWallet.Core.Crypto;
using PalladiumWallet.Core.Storage;
using PalladiumWallet.Core.Wallet;

namespace PalladiumWallet.App.ViewModels;

public partial class MainWindowViewModel
{
    // ---- wizard di setup

    public const string StepDataLocation = "data-location";
    public const string StepStart = "start";
    public const string StepChooseWallet = "choose-wallet";
    public const string StepOpen = "open";
    public const string StepShowSeed = "show-seed";
    public const string StepConfirmSeed = "confirm-seed";
    public const string StepWords = "words";
    public const string StepPassphrase = "passphrase";
    public const string StepScriptType = "script-type";
    public const string StepImportXkey = "import-xkey";
    public const string StepImportWif = "import-wif";
    public const string StepPassword = "password";

    private enum WizardFlowKind { New, Restore, ImportXkey, ImportWif }
    private WizardFlowKind _wizardFlow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepDataLocation))]
    [NotifyPropertyChangedFor(nameof(IsStepStart))]
    [NotifyPropertyChangedFor(nameof(IsStepChooseWallet))]
    [NotifyPropertyChangedFor(nameof(IsStepOpen))]
    [NotifyPropertyChangedFor(nameof(IsStepShowSeed))]
    [NotifyPropertyChangedFor(nameof(IsStepConfirmSeed))]
    [NotifyPropertyChangedFor(nameof(IsStepWords))]
    [NotifyPropertyChangedFor(nameof(IsStepPassphrase))]
    [NotifyPropertyChangedFor(nameof(IsStepScriptType))]
    [NotifyPropertyChangedFor(nameof(IsStepImportXkey))]
    [NotifyPropertyChangedFor(nameof(IsStepImportWif))]
    [NotifyPropertyChangedFor(nameof(IsStepPassword))]
    private string setupStep = StepStart;

    public bool IsStepDataLocation => SetupStep == StepDataLocation;
    public bool IsStepStart        => SetupStep == StepStart;
    public bool IsStepChooseWallet => SetupStep == StepChooseWallet;
    public bool IsStepOpen         => SetupStep == StepOpen;
    public bool IsStepShowSeed     => SetupStep == StepShowSeed;
    public bool IsStepConfirmSeed  => SetupStep == StepConfirmSeed;
    public bool IsStepWords        => SetupStep == StepWords;
    public bool IsStepPassphrase   => SetupStep == StepPassphrase;
    public bool IsStepScriptType   => SetupStep == StepScriptType;
    public bool IsStepImportXkey   => SetupStep == StepImportXkey;
    public bool IsStepImportWif    => SetupStep == StepImportWif;
    public bool IsStepPassword     => SetupStep == StepPassword;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLegacySelected))]
    [NotifyPropertyChangedFor(nameof(IsWrappedSegwitSelected))]
    [NotifyPropertyChangedFor(nameof(IsNativeSegwitSelected))]
    [NotifyPropertyChangedFor(nameof(IsTaprootSelected))]
    private ScriptKind selectedScriptKind = ScriptKind.NativeSegwit;

    public bool IsLegacySelected        => SelectedScriptKind == ScriptKind.Legacy;
    public bool IsWrappedSegwitSelected => SelectedScriptKind == ScriptKind.WrappedSegwit;
    public bool IsNativeSegwitSelected  => SelectedScriptKind == ScriptKind.NativeSegwit;
    public bool IsTaprootSelected       => SelectedScriptKind == ScriptKind.Taproot;

    [RelayCommand] private void SelectLegacy()        => SelectedScriptKind = ScriptKind.Legacy;
    [RelayCommand] private void SelectWrappedSegwit() => SelectedScriptKind = ScriptKind.WrappedSegwit;
    [RelayCommand] private void SelectNativeSegwit()  => SelectedScriptKind = ScriptKind.NativeSegwit;
    [RelayCommand] private void SelectTaproot()       => SelectedScriptKind = ScriptKind.Taproot;

    public string DefaultDataPath => AppPaths.DefaultDataRoot();

    [ObservableProperty]
    private string importXkeyInput = "";

    [ObservableProperty]
    private string importWifInput = "";

    // Tipo di script rilevato durante la decodifica dell'xkey (per mostrarlo all'utente)
    [ObservableProperty]
    private string importXkeyDetectedKind = "";

    [ObservableProperty]
    private string mnemonicInput = "";

    [ObservableProperty]
    private string confirmMnemonicInput = "";

    [ObservableProperty]
    private string passphraseInput = "";

    [ObservableProperty]
    private string passwordInput = "";

    [ObservableProperty]
    private string confirmPasswordInput = "";

    [ObservableProperty]
    private bool encryptWallet = true;

    [ObservableProperty]
    private bool walletFileExists;

    public ObservableCollection<WalletFileEntry> WalletList { get; } = [];

    [RelayCommand]
    private void UseDefaultDataLocation() => ApplyDataLocation(AppPaths.DefaultDataRoot());

    public void ApplyDataLocation(string root)
    {
        try
        {
            AppPaths.ConfigureDataLocation(root);
        }
        catch (Exception ex)
        {
            StatusMessage = $"{Loc.Tr("msg.error")}: {ex.Message}";
            return;
        }
        _config = AppConfig.Load();
        _loc = Loc.SwitchTo(_config.Language);
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(UnitLabel));
        RefreshSetupState();
    }

    private void RefreshSetupState()
    {
        SetupStep = StepStart;
        SelectedScriptKind = ScriptKind.NativeSegwit;
        MnemonicInput = ConfirmMnemonicInput = PassphraseInput = PasswordInput = ConfirmPasswordInput = "";
        WalletFileExists = AppPaths.WalletFiles(Net).Count > 0;
        StatusMessage = "";
        RefreshServers();
        _ = ConnectAndSync();
    }

    [RelayCommand]
    private void WizardStartOpen()
    {
        var files = AppPaths.WalletFiles(Net);
        if (files.Count > 1)
        {
            WalletList.Clear();
            foreach (var path in files)
                WalletList.Add(new WalletFileEntry(Path.GetFileName(path), path));
            SetupStep = StepChooseWallet;
            StatusMessage = "";
            return;
        }
        _pendingOpenPath = files.Count == 1 ? files[0] : AppPaths.DefaultWalletPath(Net);
        PasswordInput = "";
        SetupStep = StepOpen;
        StatusMessage = "";
    }

    [RelayCommand]
    private void ChooseWallet(WalletFileEntry? entry)
    {
        if (entry is null)
            return;
        _pendingOpenPath = entry.Path;
        PasswordInput = "";
        SetupStep = StepOpen;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardStartNew()
    {
        _wizardFlow = WizardFlowKind.New;
        MnemonicInput = Bip39.Generate(MnemonicLength.Twelve).ToString();
        SetupStep = StepShowSeed;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardStartRestore()
    {
        _wizardFlow = WizardFlowKind.Restore;
        MnemonicInput = "";
        SetupStep = StepWords;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardStartImportXkey()
    {
        _wizardFlow = WizardFlowKind.ImportXkey;
        ImportXkeyInput = "";
        ImportXkeyDetectedKind = "";
        SetupStep = StepImportXkey;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardStartImportWif()
    {
        _wizardFlow = WizardFlowKind.ImportWif;
        ImportWifInput = "";
        SetupStep = StepImportWif;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardNextFromShowSeed()
    {
        ConfirmMnemonicInput = "";
        SetupStep = StepConfirmSeed;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardNextFromConfirmSeed()
    {
        var normalized = string.Join(' ',
            ConfirmMnemonicInput.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!string.Equals(normalized, MnemonicInput, StringComparison.OrdinalIgnoreCase))
        {
            StatusMessage = Loc.Tr("msg.seed.mismatch");
            return;
        }
        GoToPassphraseStep();
    }

    [RelayCommand]
    private void WizardNextFromWords()
    {
        if (!Bip39.TryParse(MnemonicInput, out _))
        {
            StatusMessage = Loc.Tr("msg.words.invalid");
            return;
        }
        GoToPassphraseStep();
    }

    private void GoToPassphraseStep()
    {
        PassphraseInput = "";
        SetupStep = StepPassphrase;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardNextFromPassphrase()
    {
        SetupStep = StepScriptType;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardNextFromImportXkey()
    {
        if (string.IsNullOrWhiteSpace(ImportXkeyInput))
        {
            StatusMessage = Loc.Tr("msg.xkey.required");
            return;
        }
        // Valida e rileva il tipo: prova prima come xpub, poi come xprv.
        if (Slip132.TryDecodePublic(ImportXkeyInput.Trim(), Profile, out _, out var pubKind))
        {
            SelectedScriptKind = pubKind;
            ImportXkeyDetectedKind = $"{pubKind} (watch-only)";
        }
        else if (Slip132.TryDecodePrivate(ImportXkeyInput.Trim(), Profile, out _, out var prvKind))
        {
            SelectedScriptKind = prvKind;
            ImportXkeyDetectedKind = prvKind.ToString();
        }
        else
        {
            StatusMessage = Loc.Tr("msg.xkey.invalid");
            return;
        }
        PasswordInput = ConfirmPasswordInput = "";
        EncryptWallet = true;
        SetupStep = StepPassword;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardNextFromImportWif()
    {
        if (string.IsNullOrWhiteSpace(ImportWifInput))
        {
            StatusMessage = Loc.Tr("msg.wif.required");
            return;
        }
        // Valida il WIF con un parsing anticipato.
        try
        {
            _ = new NBitcoin.BitcoinSecret(ImportWifInput.Trim(), PalladiumNetworks.For(Net));
        }
        catch
        {
            StatusMessage = Loc.Tr("msg.wif.invalid");
            return;
        }
        SetupStep = StepScriptType;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardNextFromScriptType()
    {
        PasswordInput = ConfirmPasswordInput = "";
        EncryptWallet = true;
        SetupStep = StepPassword;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardBack()
    {
        SetupStep = SetupStep switch
        {
            StepOpen => WalletList.Count > 1 ? StepChooseWallet : StepStart,
            StepChooseWallet or StepShowSeed or StepWords or StepImportXkey or StepImportWif => StepStart,
            StepConfirmSeed => StepShowSeed,
            StepPassphrase => _wizardFlow == WizardFlowKind.Restore ? StepWords : StepConfirmSeed,
            StepScriptType => _wizardFlow == WizardFlowKind.ImportWif ? StepImportWif : StepPassphrase,
            StepPassword => _wizardFlow == WizardFlowKind.ImportXkey ? StepImportXkey : StepScriptType,
            _ => StepStart,
        };
        if (SetupStep == StepStart)
            RefreshSetupState();
    }

    [RelayCommand]
    private void CreateOrRestore()
    {
        string? password;
        if (EncryptWallet)
        {
            if (string.IsNullOrEmpty(PasswordInput))
            {
                StatusMessage = Loc.Tr("msg.password.required");
                return;
            }
            if (PasswordInput != ConfirmPasswordInput)
            {
                StatusMessage = Loc.Tr("msg.password.mismatch");
                return;
            }
            password = PasswordInput;
        }
        else
        {
            password = null;
        }

        try
        {
            WalletDocument doc;
            IWalletAccount account;

            switch (_wizardFlow)
            {
                case WizardFlowKind.ImportXkey:
                {
                    var input = ImportXkeyInput.Trim();
                    if (Slip132.TryDecodePublic(input, Profile, out _, out _))
                    {
                        var (d, a) = WalletLoader.NewFromXpub(input, Profile, SelectedScriptKind);
                        (doc, account) = (d, a);
                    }
                    else
                    {
                        var (d, a) = WalletLoader.NewFromXprv(input, Profile, SelectedScriptKind);
                        (doc, account) = (d, a);
                    }
                    break;
                }
                case WizardFlowKind.ImportWif:
                {
                    var wifLines = ImportWifInput.Split('\n',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var (d, a) = WalletLoader.NewFromWif(wifLines, SelectedScriptKind, Profile);
                    (doc, account) = (d, a);
                    break;
                }
                default:
                {
                    var (d, a) = WalletLoader.NewFromMnemonic(
                        MnemonicInput,
                        string.IsNullOrEmpty(PassphraseInput) ? null : PassphraseInput,
                        SelectedScriptKind,
                        Profile);
                    (doc, account) = (d, a);
                    break;
                }
            }

            var path = AppPaths.DefaultWalletPath(Net);
            for (var n = 2; WalletStore.Exists(path); n++)
                path = Path.Combine(AppPaths.WalletsDir(Net), $"wallet-{n}.wallet.json");
            WalletStore.Save(doc, path, password);
            var newLock = WalletLock.TryAcquire(path);
            if (newLock is null) { StatusMessage = Loc.Tr("msg.wallet.locked"); return; }
            OpenLoaded(doc, account, path, password, newLock);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    [RelayCommand]
    private void OpenExisting()
    {
        var path = _pendingOpenPath ?? AppPaths.DefaultWalletPath(Net);
        var password = string.IsNullOrEmpty(PasswordInput) ? null : PasswordInput;

        var newLock = WalletLock.TryAcquire(path);
        if (newLock is null) { StatusMessage = Loc.Tr("msg.wallet.locked"); return; }

        try
        {
            var doc = WalletStore.Load(path, password);
            _pendingOpenPath = null;
            OpenLoaded(doc, WalletLoader.ToAccount(doc), path, password, newLock);
        }
        catch (WrongPasswordException)
        {
            newLock.Dispose();
            StatusMessage = Loc.Tr("msg.wrongpassword");
        }
        catch (UnauthorizedAccessException)
        {
            newLock.Dispose();
            StatusMessage = Loc.Tr("msg.wallet.noaccess");
        }
        catch (Exception ex)
        {
            newLock.Dispose();
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    public void OpenFromPath(string path)
    {
        var newLock = WalletLock.TryAcquire(path);
        if (newLock is null) { StatusMessage = Loc.Tr("msg.wallet.locked"); return; }

        try
        {
            var doc = WalletStore.Load(path);
            if (IsWalletOpen)
                CloseWallet();
            OpenLoaded(doc, WalletLoader.ToAccount(doc), path, password: null, newLock);
        }
        catch (WrongPasswordException)
        {
            newLock.Dispose();
            if (IsWalletOpen)
                CloseWallet();
            _pendingOpenPath = path;
            WalletFileExists = true;
            PasswordInput = "";
            SetupStep = StepOpen;
            StatusMessage = "";
        }
        catch (UnauthorizedAccessException)
        {
            newLock.Dispose();
            StatusMessage = Loc.Tr("msg.wallet.noaccess");
        }
        catch (Exception ex)
        {
            newLock.Dispose();
            StatusMessage = $"Errore: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewWallet()
    {
        if (IsWalletOpen)
            CloseWallet();
        _pendingOpenPath = null;
        StatusMessage = "";
    }

    private void OpenLoaded(
        WalletDocument doc, IWalletAccount account, string path, string? password, WalletLock walletLock)
    {
        _walletLock?.Dispose();
        _walletLock = walletLock;

        _doc = doc;
        _account = account;
        _walletPath = path;
        _password = password;
        MnemonicInput = ConfirmMnemonicInput = PassphraseInput = PasswordInput = ConfirmPasswordInput = "";
        ImportXkeyInput = ImportWifInput = ImportXkeyDetectedKind = "";
        SetupStep = StepStart;

        var walletKindTag = account switch
        {
            ImportedKeyAccount => " · imported",
            { IsWatchOnly: true } => " · watch-only",
            _ => ""
        };
        var pathTag = !string.IsNullOrEmpty(doc.AccountPath) ? $" · m/{doc.AccountPath}" : "";
        NetworkInfo = $"{doc.Network} · {doc.ScriptKind}{pathTag}{walletKindTag}";
        LoadContacts();
        ApplyCache(doc.Cache);
        IsWalletOpen = true;
        StatusMessage = Loc.Tr("msg.opened");
        _autoReconnect = true;  // keepalive riprova la connessione anche se il primo tentativo fallisce
        _syncFailed = true;     // forza la prima sync automatica
        _ = ConnectAndSync();
    }
}