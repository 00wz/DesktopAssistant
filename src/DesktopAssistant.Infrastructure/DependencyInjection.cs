using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Application.Services;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.AI;
using DesktopAssistant.Infrastructure.AI.Executors;
using DesktopAssistant.Infrastructure.AI.Kernel;
using DesktopAssistant.Infrastructure.AI.Services;
using DesktopAssistant.Infrastructure.AI.Session;
using DesktopAssistant.Infrastructure.MCP.Search;
using DesktopAssistant.Infrastructure.MCP.Services;
using DesktopAssistant.Infrastructure.Persistence;
using DesktopAssistant.Infrastructure.Persistence.Repositories;
using DesktopAssistant.Infrastructure.Security;
using DesktopAssistant.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace DesktopAssistant.Infrastructure;

/// <summary>
/// Extensions for registering infrastructure layer services
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        var connectionString = configuration["Database:ConnectionString"]
            ?? "Data Source=desktop_assistant.db";

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repositories
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageNodeRepository, MessageNodeRepository>();
        services.AddScoped<IAssistantProfileRepository, AssistantProfileRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();

        // Security — DPAPI storage for API keys
        services.AddScoped<ISecureCredentialStore, DpapiCredentialStore>();

        // Application Services
        services.AddScoped<ConversationService>();

        // AI Services
        services.AddSingleton<IKernelFactory, KernelFactory>();
        services.AddSingleton<AgentKernelFactory>();
        services.AddScoped<LlmTurnExecutor>();
        services.AddScoped<ToolCallExecutor>();
        services.AddScoped<ISummarizationService, SummarizationExecutor>();
        services.AddScoped<IAssistantProfileService, AssistantProfileService>();
        services.AddScoped<IChatService, ChatService>(); 
        services.AddSingleton<IConversationSessionService, ConversationSessionService>();
        services.AddSingleton<ISubagentService, SubagentService>();


        // MCP Services
        services.AddSingleton<IMcpConfigurationService, McpConfigurationService>();
        services.AddSingleton<IMcpServerManager, McpServerManager>();
        services.AddSingleton<IMcpCatalogSearchService, KeywordMcpCatalogSearchService>();

        // Tool approval and discovery
        services.AddSingleton<IToolApprovalService, ToolApprovalService>();
        services.AddSingleton<IAvailableToolsProvider, AvailableToolsService>();

        // Localization
        services.AddSingleton<ILocalizationService, LocalizationService>();

        // Logging
        services.AddLogging(loggingBuilder =>
        {
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File("logs/desktopassistant-.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            loggingBuilder.AddSerilog(logger, dispose: true);
        });

        return services;
    }

    public static async Task InitializeDatabaseAsync(this IServiceProvider serviceProvider)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        try
        {
            await dbContext.Database.EnsureCreatedAsync();
            // Quick schema compatibility check
            await dbContext.Conversations.CountAsync();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Database schema is incompatible with the current model. " +
                "Dropping and recreating the database (all existing data will be lost).");
            await dbContext.Database.EnsureDeletedAsync();
            await dbContext.Database.EnsureCreatedAsync();
        }
    }
}
