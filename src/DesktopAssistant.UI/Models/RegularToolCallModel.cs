using CommunityToolkit.Mvvm.ComponentModel;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// A regular (non-terminal) tool call that requires explicit user approval or denial.
/// Displayed as a collapsible card showing plugin.function, arguments, and result.
/// Can transition: Pending → Executing → Completed | Failed | Denied.
/// </summary>
public partial class RegularToolCallModel : ToolChatMessageModel
{
    /// <summary>JSON-encoded arguments passed to the tool.</summary>
    [ObservableProperty]
    private string _argumentsJson = string.Empty;
}
