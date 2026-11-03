using BookClubApp.App.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;

namespace BookClubApp.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Local development server.
#if ANDROID
        string serverAddress = "http://10.0.2.2:5240/";
#else
        string serverAddress = "http://localhost:5240/";
#endif

        builder.Services.AddSingleton<HttpClient>(_ => new HttpClient
        {
            BaseAddress = new Uri(serverAddress),
            Timeout = TimeSpan.FromSeconds(30)
        });

        builder.Services.AddSingleton<CommentDatabase>();
        builder.Services.AddSingleton<PushSyncService>();
        builder.Services.AddSingleton<PullSyncService>();
        builder.Services.AddTransient<MainPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}