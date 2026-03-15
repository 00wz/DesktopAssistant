using Avalonia;
using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using DesktopAssistant.Application.Interfaces;
using DesktopAssistant.Infrastructure;
using DesktopAssistant.UI.Localization;
using DesktopAssistant.UI.ViewModels;

namespace DesktopAssistant.UI;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Configure configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .Build();
        
        // Configure DI
        var services = new ServiceCollection();
        
        // Register infrastructure services
        services.AddInfrastructure(configuration);
        
        // Register ViewModels
        services.AddTransient<ChatSettingsPanelViewModel>();
        services.AddTransient<ChatViewModel>();
        services.AddTransient<SidebarViewModel>();
        services.AddTransient<NewConversationPanelViewModel>();
        services.AddTransient<ProfileEditorViewModel>();
        services.AddTransient<ProfilesSettingsViewModel>();
        services.AddTransient<ToolApprovalSettingsViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainWindowViewModel>();

        var serviceProvider = services.BuildServiceProvider();

        // Initialize database
        serviceProvider.InitializeDatabaseAsync().GetAwaiter().GetResult();

        // Load saved language before starting Avalonia
        var locService = serviceProvider.GetRequiredService<ILocalizationService>();
        var savedLanguage = locService.GetSavedLanguageAsync().GetAwaiter().GetResult();
        LocalizationManager.Instance.PendingLanguage = savedLanguage;
        
        // Initialize MCP servers (in background, does not block startup)
        InitializeMcpServersAsync(serviceProvider);
        
        // Pass ServiceProvider to the application
        App.SetServiceProvider(serviceProvider);
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
    
    /// <summary>
    /// Asynchronously initializes MCP servers in the background
    /// </summary>
    private static async void InitializeMcpServersAsync(IServiceProvider serviceProvider)
    {
        try
        {
            var mcpManager = serviceProvider.GetRequiredService<IMcpServerManager>();
            await mcpManager.InitializeAsync();
        }
        catch (Exception ex)
        {
            var logger = serviceProvider.GetService<ILogger<Program>>();
            logger?.LogError(ex, "Failed to initialize MCP servers");
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
