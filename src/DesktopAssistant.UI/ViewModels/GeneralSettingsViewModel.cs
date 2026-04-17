using CommunityToolkit.Mvvm.ComponentModel;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.UI.Localization;
using DesktopAssistant.UI.Models;

namespace DesktopAssistant.UI.ViewModels;

/// <summary>
/// ViewModel for the "General" section of the settings panel.
/// Manages UI language selection and summarization schema selection.
/// </summary>
public partial class GeneralSettingsViewModel : ObservableObject
{
    private readonly ILocalizationService _localizationService;
    private readonly ISummarizationSchemaService _schemaService;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
        LocalizationManager.AvailableLanguages;

    public IReadOnlyList<SummarizationSchemaOption> AvailableSchemas { get; } =
    [
        new(SummarizationSchema.ToolInteraction, "tool_interaction — compact, bundled call+result"),
        new(SummarizationSchema.PairedCall,      "paired call/result — standard function_call format"),
    ];

    [ObservableProperty]
    private LanguageOption? _selectedLanguage;

    [ObservableProperty]
    private SummarizationSchemaOption? _selectedSchema;

    private bool _isSchemaSaving;

    public GeneralSettingsViewModel(
        ILocalizationService localizationService,
        ISummarizationSchemaService schemaService)
    {
        _localizationService = localizationService;
        _schemaService = schemaService;

        _selectedLanguage = AvailableLanguages
            .FirstOrDefault(l => l.Code == LocalizationManager.Instance.CurrentLanguage)
            ?? AvailableLanguages[0];
    }

    /// <summary>
    /// Loads persisted settings from the data store. Call once when the section becomes visible.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var schema = await _schemaService.GetSchemaAsync(cancellationToken);

        _isSchemaSaving = true;
        try
        {
            SelectedSchema = AvailableSchemas.FirstOrDefault(o => o.Schema == schema)
                ?? AvailableSchemas[0];
        }
        finally
        {
            _isSchemaSaving = false;
        }
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value is null) return;

        LocalizationManager.Instance.LoadLanguage(value.Code);
        _ = _localizationService.SetLanguageAsync(value.Code);
    }

    partial void OnSelectedSchemaChanged(SummarizationSchemaOption? value)
    {
        if (value is null || _isSchemaSaving) return;

        _ = _schemaService.SetSchemaAsync(value.Schema);
    }
}
