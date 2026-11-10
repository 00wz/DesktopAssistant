using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesktopAssistant.UI.ViewModels;

public enum SettingsSection { General, Profiles, ToolApproval, Help }

/// <summary>
/// Wrapper ViewModel for the settings panel. Manages navigation between sections
/// and provides an entry point for closing the panel.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    public GeneralSettingsViewModel GeneralSettings { get; }
    public ProfilesSettingsViewModel ProfilesSettings { get; }
    public ToolApprovalSettingsViewModel ToolApprovalSettings { get; }

    /// <summary>Set by the main window to hide the settings panel.</summary>
    public Action? OnClose { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralActive))]
    [NotifyPropertyChangedFor(nameof(IsProfilesActive))]
    [NotifyPropertyChangedFor(nameof(IsToolApprovalActive))]
    [NotifyPropertyChangedFor(nameof(IsHelpActive))]
    private SettingsSection _activeSection = SettingsSection.General;

    public bool IsGeneralActive => ActiveSection == SettingsSection.General;
    public bool IsProfilesActive => ActiveSection == SettingsSection.Profiles;
    public bool IsToolApprovalActive => ActiveSection == SettingsSection.ToolApproval;
    public bool IsHelpActive => ActiveSection == SettingsSection.Help;

    public SettingsViewModel(
        GeneralSettingsViewModel generalSettings,
        ProfilesSettingsViewModel profilesSettings,
        ToolApprovalSettingsViewModel toolApprovalSettings)
    {
        GeneralSettings = generalSettings;
        ProfilesSettings = profilesSettings;
        ToolApprovalSettings = toolApprovalSettings;
    }

    [RelayCommand]
    private void Close() => OnClose?.Invoke();

    [RelayCommand]
    private void ShowGeneral()
    {
        ActiveSection = SettingsSection.General;
    }

    [RelayCommand]
    private void ShowProfiles()
    {
        ActiveSection = SettingsSection.Profiles;
    }

    [RelayCommand]
    private async Task ShowToolApprovalAsync()
    {
        ActiveSection = SettingsSection.ToolApproval;
        await ToolApprovalSettings.LoadAsync();
    }

    [RelayCommand]
    private void ShowHelp()
    {
        ActiveSection = SettingsSection.Help;
    }

    public void Dispose()
    {
        ToolApprovalSettings.Dispose();
    }
}
