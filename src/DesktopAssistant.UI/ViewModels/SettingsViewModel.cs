using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesktopAssistant.UI.ViewModels;

public enum SettingsSection { Profiles, ToolApproval }

/// <summary>
/// ViewModel-оболочка панели настроек. Управляет навигацией между секциями
/// и предоставляет точку входа для закрытия панели.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    public ProfilesSettingsViewModel ProfilesSettings { get; }
    public ToolApprovalSettingsViewModel ToolApprovalSettings { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsProfilesActive))]
    [NotifyPropertyChangedFor(nameof(IsToolApprovalActive))]
    private SettingsSection _activeSection = SettingsSection.Profiles;

    public bool IsProfilesActive => ActiveSection == SettingsSection.Profiles;
    public bool IsToolApprovalActive => ActiveSection == SettingsSection.ToolApproval;

    public SettingsViewModel(
        ProfilesSettingsViewModel profilesSettings,
        ToolApprovalSettingsViewModel toolApprovalSettings)
    {
        ProfilesSettings = profilesSettings;
        ToolApprovalSettings = toolApprovalSettings;
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
