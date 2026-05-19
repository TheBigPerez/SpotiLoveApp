using Microsoft.Extensions.Logging;

namespace SpotiLove;

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

        // Enable deep linking for Spotify callback
        builder.Services.AddSingleton<SpotifyAuthHandler>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
