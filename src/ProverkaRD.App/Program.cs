using Avalonia;
using System;

namespace ProverkaRD;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Any(a => a is "--check" or "--help" or "-h"))
        {
            Environment.ExitCode = Headless.Run(args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
