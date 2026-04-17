using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// Represents a selectable <see cref="SummarizationSchema"/> entry for display in the settings UI.
/// </summary>
public sealed record SummarizationSchemaOption(SummarizationSchema Schema, string DisplayName);
