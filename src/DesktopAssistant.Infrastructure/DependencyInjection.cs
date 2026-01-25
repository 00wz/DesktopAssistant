using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Domain.Interfaces;
using DesktopAssistant.Infrastructure.Persistence;
using DesktopAssistant.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace DesktopAssistant.Infrastructure;

/// <summary>
/// Расширения для регистрации сервисов инфраструктурного слоя
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        var connectionString = configuration.GetConnectionString("DefaultConnection") 
            ?? "Data Source=desktopassistant.db";
        
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        // Repositories
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageNodeRepository, MessageNodeRepository>();
        services.AddScoped<IConversationBranchRepository, ConversationBranchRepository>();
        services.AddScoped<IAssistantProfileRepository, AssistantProfileRepository>();
        services.AddScoped<IAppSettingsRepository, AppSettingsRepository>();

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
        
        await dbContext.Database.MigrateAsync();
    }
}
