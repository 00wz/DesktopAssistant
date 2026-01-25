using Avalonia;
using System;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DesktopAssistant.Infrastructure;
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
        // Настройка конфигурации
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddUserSecrets(Assembly.GetExecutingAssembly(), optional: true)
            .Build();
        
        // Настройка DI
        var services = new ServiceCollection();
        
        // Регистрация инфраструктурных сервисов
        services.AddInfrastructure(configuration);
        
        // Регистрация ViewModels
        services.AddTransient<ChatViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Инициализация базы данных
        serviceProvider.InitializeDatabaseAsync().GetAwaiter().GetResult();
        
        // Передаём ServiceProvider в приложение
        App.SetServiceProvider(serviceProvider);
        
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
