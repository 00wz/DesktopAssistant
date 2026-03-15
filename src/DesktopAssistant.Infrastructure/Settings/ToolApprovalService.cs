using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopAssistant.Infrastructure.Settings;

/// <summary>
/// Stores auto-approve settings for each tool in the AppSettings table.
/// Key: "tool-autoapprove:{pluginName}:{functionName}".
/// Default (no entry) — approval is required (false).
/// </summary>
public class ToolApprovalService : IToolApprovalService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ToolApprovalService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<bool> IsAutoApprovedAsync(string pluginName, string functionName)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>();
        var value = await repo.GetValueAsync(BuildKey(pluginName, functionName));
        return value == "true";
    }

    public async Task SetAutoApprovedAsync(string pluginName, string functionName, bool value)
    {
        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IAppSettingsRepository>();
        await repo.SetAsync(
            BuildKey(pluginName, functionName),
            value ? "true" : "false",
            $"Auto-approve for {pluginName}.{functionName}");
    }

    private static string BuildKey(string pluginName, string functionName)
        => $"tool-autoapprove:{pluginName}:{functionName}";
}
