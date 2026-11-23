using System.Net.Http;
using System.Windows;
using FurnitureWorkshop.Api;
using Microsoft.AspNetCore.Builder;

namespace FurnitureWorkshop.Desktop;

public partial class App : Application
{
    private WebApplication? host;
    private HttpClient? http;
    private Task<WorkshopClient>? client;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ClientLog.Start();
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage("ru-RU")));
        client = Task.Run(StartClient);
        ShowLogin();
    }

    private async Task<WorkshopClient> StartClient()
    {
        host = WorkshopHost.Build([], desktop: true);
        await WorkshopHost.Initialize(host);
        await host.StartAsync();
        http = new HttpClient { BaseAddress = new Uri(host.Urls.Single()), Timeout = TimeSpan.FromSeconds(12) };
        return new WorkshopClient(http);
    }

    private void ShowLogin()
    {
        var model = new LoginViewModel(client!, ShowWorkspace);
        SwitchTo(new LoginWindow { DataContext = model });
    }

    private void ShowWorkspace(WorkshopClient api, Core.LoginResult login)
    {
        var shell = new WorkspaceWindow();
        var model = new WorkspaceViewModel(api, login, new WorkshopDialogs(shell), ShowLogin);
        shell.DataContext = model;
        SwitchTo(shell);
        model.RefreshOrders.Execute(null);
    }

    private void SwitchTo(Window next)
    {
        var previous = MainWindow;
        MainWindow = next;
        next.Show();
        if (previous is not null && previous != next)
            previous.Close();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (client is not null)
                Task.WhenAny(client, Task.Delay(1500)).GetAwaiter().GetResult();
            if (client?.IsCompleted == true && host is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                host.StopAsync(timeout.Token).GetAwaiter().GetResult();
                host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception error)
        {
            ClientLog.Write(error);
        }
        finally
        {
            http?.Dispose();
            ClientLog.Info("Завершение работы WPF-клиента");
            base.OnExit(e);
        }
    }
}
