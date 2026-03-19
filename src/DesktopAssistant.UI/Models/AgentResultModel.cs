using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json;

namespace DesktopAssistant.UI.Models;

/// <summary>
/// The terminal output node of an agent sub-task.
/// Displayed as a "✓ Agent output" card showing the final result message.
/// Unlike <see cref="RegularToolCallModel"/>, it does not expose raw arguments —
/// only the human-readable message and approval buttons (if confirmation is required).
/// </summary>
public partial class AgentResultModel : ToolChatMessageModel
{
    /// <summary>
    /// Human-readable message extracted from the tool's <c>message</c> argument.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMessage))]
    private string _message = string.Empty;

    public bool HasMessage => !string.IsNullOrEmpty(Message);

    /// <summary>
    /// Extracts the <c>message</c> string from a JSON arguments object such as
    /// <c>{"message":"Task is done"}</c>. Returns an empty string on failure.
    /// </summary>
    public static string ExtractMessage(string argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return string.Empty;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.TryGetProperty("message", out var prop))
                return prop.GetString() ?? string.Empty;
        }
        catch { /* malformed JSON — fall through */ }
        return string.Empty;
    }
}
