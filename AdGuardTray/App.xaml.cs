using System;
using System.Windows;
using AdGuardTray.Configuration;
using AdGuardTray.Services;
using AdGuardTray.Tray;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AdGuardTray;

public partial class App : Application
{
    public static IHost Host { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        Host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.json",
                    optional: false,
                    reloadOnChange: true);
            })
            .ConfigureServices((context, services) =>
            {
                // Load settings from appsettings.json
                services.Configure<AdGuardSettings>(
                    context.Configuration.GetSection("AdGuard"));

                // Register the tray manager
                services.AddSingleton<TrayManager>();

                // Register the AdGuard API client
                services.AddHttpClient<AdGuardApiClient>((provider, client) =>
                {
                    var settings = provider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<AdGuardSettings>>();

                    client.BaseAddress = new Uri(settings.Value.BaseUrl);
                    client.Timeout = TimeSpan.FromSeconds(10);
                });
            })
            .Build();

        await Host.StartAsync();

        // Start the tray manager
        Host.Services.GetRequiredService<TrayManager>();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await Host.StopAsync();
        Host.Dispose();

        base.OnExit(e);
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        Shutdown();
    }
}