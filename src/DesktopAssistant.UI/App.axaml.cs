using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using System.Linq;
using Avalonia.Markup.Xaml;
using DesktopAssistant.UI.Localization;
using DesktopAssistant.UI.ViewModels;
using DesktopAssistant.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopAssistant.UI;

public partial class App : Avalonia.Application
{
    public static IServiceProvider? ServiceProvider { get; private set; }
    
    public override void Initialize()
    {
        // Загружаем словарь локализации до обработки App.axaml,
        // чтобы все {DynamicResource} сразу получили актуальные строки.
        LocalizationManager.Instance.LoadLanguage(LocalizationManager.Instance.PendingLanguage);
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            if (ServiceProvider == null)
            {
                throw new InvalidOperationException("ServiceProvider not initialized. Call App.SetServiceProvider() first.");
            }
            
            var mainWindowViewModel = ServiceProvider.GetRequiredService<MainWindowViewModel>();
            
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainWindowViewModel,
            };
            
            // Инициализируем MainWindow асинхронно
            _ = mainWindowViewModel.InitializeAsync();
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    public static void SetServiceProvider(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}
