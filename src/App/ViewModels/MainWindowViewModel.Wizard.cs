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
    public const string StepPassword = "password";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStepDataLocation))]
    [NotifyPropertyChangedFor(nameof(IsStepStart))]
    [NotifyPropertyChangedFor(nameof(IsStepChooseWallet))]
    [NotifyPropertyChangedFor(nameof(IsStepOpen))]
    [NotifyPropertyChangedFor(nameof(IsStepShowSeed))]
    [NotifyPropertyChangedFor(nameof(IsStepConfirmSeed))]
    [NotifyPropertyChangedFor(nameof(IsStepWords))]
    [NotifyPropertyChangedFor(nameof(IsStepPassphrase))]
    [NotifyPropertyChangedFor(nameof(IsStepPassword))]
    private string setupStep = StepStart;

    public bool IsStepDataLocation => SetupStep == StepDataLocation;
    public bool IsStepStart       => SetupStep == StepStart;
    public bool IsStepChooseWallet => SetupStep == StepChooseWallet;
    public bool IsStepOpen        => SetupStep == StepOpen;
    public bool IsStepShowSeed    => SetupStep == StepShowSeed;
    public bool IsStepConfirmSeed => SetupStep == StepConfirmSeed;
    public bool IsStepWords       => SetupStep == StepWords;
    public bool IsStepPassphrase  => SetupStep == StepPassphrase;
    public bool IsStepPassword    => SetupStep == StepPassword;

    public string DefaultDataPath => AppPaths.DefaultDataRoot();

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
        _isRestoreFlow = false;
        MnemonicInput = Bip39.Generate(MnemonicLength.Twelve).ToString();
        SetupStep = StepShowSeed;
        StatusMessage = "";
    }

    [RelayCommand]
    private void WizardStartRestore()
    {
        _isRestoreFlow = true;
        MnemonicInput = "";
        SetupStep = StepWords;
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
            StepChooseWallet or StepShowSeed or StepWords => StepStart,
            StepConfirmSeed => StepShowSeed,
            StepPassphrase => _isRestoreFlow ? StepWords : StepConfirmSeed,
            StepPassword => StepPassphrase,
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
            var (doc, account) = WalletLoader.NewFromMnemonic(
                MnemonicInput,
                string.IsNullOrEmpty(PassphraseInput) ? null : PassphraseInput,
                ScriptKind.NativeSegwit,
                Profile);
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
        WalletDocument doc, HdAccount account, string path, string? password, WalletLock walletLock)
    {
        _walletLock?.Dispose();
        _walletLock = walletLock;

        SelectedNetwork = doc.Network;
        _doc = doc;
        _account = account;
        _walletPath = path;
        _password = password;
        MnemonicInput = ConfirmMnemonicInput = PassphraseInput = PasswordInput = ConfirmPasswordInput = "";
        SetupStep = StepStart;

        NetworkInfo = $"{doc.Network} · {doc.ScriptKind} · m/{doc.AccountPath}"
            + (doc.IsWatchOnly ? " · watch-only" : "");
        LoadContacts();
        ApplyCache(doc.Cache);
        IsWalletOpen = true;
        StatusMessage = Loc.Tr("msg.opened");
        _ = ConnectAndSync();
    }
}