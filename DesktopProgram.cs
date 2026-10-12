using Avalonia;

namespace InternetPro.Platforms.Shared;

public static class DesktopProgram
{
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}