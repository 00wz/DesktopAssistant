namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Service that stores auto-approval settings for tool calls.
/// Allows configuring an auto-approve policy separately for each tool.
/// </summary>
public interface IToolApprovalService
{
    /// <summary>
    /// Returns true if the tool identified by pluginName/functionName is configured
    /// for automatic execution without a confirmation prompt.
    /// </summary>
    Task<bool> IsAutoApprovedAsync(string pluginName, string functionName);

    /// <summary>
    /// Saves the auto-approve setting for a specific tool.
    /// </summary>
    Task SetAutoApprovedAsync(string pluginName, string functionName, bool value);
}
