using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Entities;
using DesktopAssistant.Domain.Enums;
using DesktopAssistant.Domain.Interfaces;

namespace DesktopAssistant.Infrastructure.Settings;

/// <summary>
/// Persists and retrieves the <see cref="SummarizationSchema"/> setting via <see cref="IAppSettingsRepository"/>.
/// Falls back to <see cref="SummarizationSchema.ToolInteraction"/> when the key is absent or contains
/// an unrecognised value.
/// </summary>
public sealed class SummarizationSchemaService(IAppSettingsRepository appSettingsRepository)
    : ISummarizationSchemaService
{
    private readonly IAppSettingsRepository _appSettingsRepository = appSettingsRepository;

    /// <inheritdoc/>
    public async Task<SummarizationSchema> GetSchemaAsync(CancellationToken cancellationToken = default)
    {
        var raw = await _appSettingsRepository.GetValueAsync(
            AppSettings.Keys.SummarizationSchema, cancellationToken);

        return Enum.TryParse<SummarizationSchema>(raw, ignoreCase: true, out var schema)
            ? schema
            : SummarizationSchema.ToolInteraction;
    }

    /// <inheritdoc/>
    public async Task SetSchemaAsync(SummarizationSchema schema, CancellationToken cancellationToken = default)
    {
        await _appSettingsRepository.SetAsync(
            AppSettings.Keys.SummarizationSchema, schema.ToString(), cancellationToken: cancellationToken);
    }
}
