using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;

namespace TwinZMultiChat;

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
            })
            .UseMauiCommunityToolkit(); // Add this line to configure the Maui application to use the Maui Community Toolkit

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

