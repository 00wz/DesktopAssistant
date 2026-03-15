using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesktopAssistant.UI.ViewModels;

public enum SettingsSection { General, Profiles, ToolApproval }

/// <summary>
/// Wrapper ViewModel for the settings panel. Manages navigation between sections
/// and provides an entry point for closing the panel.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    public GeneralSettingsViewModel GeneralSettings { get; }
    public ProfilesSettingsViewModel ProfilesSettings { get; }
    public ToolApprovalSettingsViewModel ToolApprovalSettings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsGeneralActive))]
    [NotifyPropertyChangedFor(nameof(IsProfilesActive))]
    [NotifyPropertyChangedFor(nameof(IsToolApprovalActive))]
    private SettingsSection _activeSection = SettingsSection.General;

    public bool IsGeneralActive => ActiveSection == SettingsSection.General;
    public bool IsProfilesActive => ActiveSection == SettingsSection.Profiles;
    public bool IsToolApprovalActive => ActiveSection == SettingsSection.ToolApproval;

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

    public void Dispose()
    {
        ToolApprovalSettings.Dispose();
    }
}
