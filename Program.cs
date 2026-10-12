using Avalonia;

namespace InternetPro.Platforms.Shared;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DesktopProgram.BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }
}