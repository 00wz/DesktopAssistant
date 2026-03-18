using DesktopAssistant.Domain.Enums;

namespace DesktopAssistant.UI.Models;

/// <summary>Display wrapper for <see cref="ConversationMode"/> used in ComboBox bindings.</summary>
public record ConversationModeOption(ConversationMode Mode, string Label)
{
    public override string ToString() => Label;
}
