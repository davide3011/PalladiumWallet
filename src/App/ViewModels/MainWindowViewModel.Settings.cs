using CommunityToolkit.Mvvm.Input;

namespace PalladiumWallet.App.ViewModels;

public partial class MainWindowViewModel
{
    // ---- spunte del menu Impostazioni (ToggleType Radio) ----

    public bool IsLangIt => _config.Language == "it";
    public bool IsLangEn => _config.Language == "en";
    public bool IsLangEs => _config.Language == "es";
    public bool IsLangFr => _config.Language == "fr";
    public bool IsLangPt => _config.Language == "pt";
    public bool IsLangDe => _config.Language == "de";
    public bool IsUnitPlm => _config.Unit == "PLM";
    public bool IsUnitMilli => _config.Unit == "mPLM";
    public bool IsUnitMicro => _config.Unit == "µPLM";
    public bool IsUnitSat => _config.Unit == "sat";

    [RelayCommand]
    private void SetLanguage(string language)
    {
        _config.Language = language;
        ApplySettings(_config);
    }

    [RelayCommand]
    private void SetUnit(string unit)
    {
        _config.Unit = unit;
        ApplySettings(_config);
    }

    public void ApplySettings(PalladiumWallet.Core.Storage.AppConfig config)
    {
        _config = config;
        _config.Save();
        _loc = Localization.Loc.SwitchTo(config.Language);
        OnPropertyChanged(nameof(Loc));
        OnPropertyChanged(nameof(UnitLabel));
        OnPropertyChanged(nameof(IsLangIt));
        OnPropertyChanged(nameof(IsLangEn));
        OnPropertyChanged(nameof(IsLangEs));
        OnPropertyChanged(nameof(IsLangFr));
        OnPropertyChanged(nameof(IsLangPt));
        OnPropertyChanged(nameof(IsLangDe));
        OnPropertyChanged(nameof(IsUnitPlm));
        OnPropertyChanged(nameof(IsUnitMilli));
        OnPropertyChanged(nameof(IsUnitMicro));
        OnPropertyChanged(nameof(IsUnitSat));
        ApplyCache(_doc?.Cache);
        StatusMessage = Localization.Loc.Tr("msg.settings.saved");
    }

    // ---- overlay impostazioni, server, help ----

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool isSettingsOpen;

    [RelayCommand]
    private void OpenSettings() => IsSettingsOpen = true;

    [RelayCommand]
    private void CloseSettings() => IsSettingsOpen = false;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    private bool isHelpOpen;

    [RelayCommand]
    private void OpenHelp() => IsHelpOpen = true;

    [RelayCommand]
    private void CloseHelp() => IsHelpOpen = false;
}
