using Avalonia;
using System;

namespace ProverkaRD;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppUpdate.DeleteBackup();
        LedgerHandoff.RestoreRequested = args.Any(a =>
            string.Equals(a, ProverkaRD.Engine.LedgerSession.RestoreArgument, StringComparison.OrdinalIgnoreCase));
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
