using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel-оболочка панели настроек. Управляет навигацией между секциями
/// и предоставляет точку входа для закрытия панели.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    /// <summary>Вызывается при закрытии панели настроек.</summary>
    public Action? OnClose { get; set; }

    public ProfilesSettingsViewModel ProfilesSettings { get; }

    public SettingsViewModel(ProfilesSettingsViewModel profilesSettings)
    {
        ProfilesSettings = profilesSettings;
    }

    [RelayCommand]
    private void Close()
    {
        OnClose?.Invoke();
    }
}
