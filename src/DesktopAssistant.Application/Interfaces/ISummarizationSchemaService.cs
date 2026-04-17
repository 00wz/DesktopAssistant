using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Reads and writes the <see cref="SummarizationSchema"/> application setting.
/// </summary>
public interface ISummarizationSchemaService
{
    /// <summary>
    /// Returns the currently configured schema.
    /// Falls back to <see cref="SummarizationSchema.ToolInteraction"/> when the setting is absent or invalid.
    /// </summary>
    Task<SummarizationSchema> GetSchemaAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the selected schema to application settings.
    /// </summary>
    Task SetSchemaAsync(SummarizationSchema schema, CancellationToken cancellationToken = default);
}
