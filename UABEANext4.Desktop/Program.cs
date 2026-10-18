using Avalonia;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace UABEANext4.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Under Wine (e.g. Winlator on Android), .NET 7+ can hang during startup because of
        // W^X (write-xor-execute) memory double-mapping. The setting can only be applied
        // before the runtime starts, so relaunch ourselves once with it disabled and end
        // this process immediately so only one instance stays in memory.
        // Does nothing on real Windows, or if the variable is already set (e.g. container env var).
        if (TryRelaunchWithoutWxorX(args))
        {
            return;
        }

#if !DEBUG
        var currentDomain = AppDomain.CurrentDomain;
        currentDomain.UnhandledException += new UnhandledExceptionEventHandler(UABEAExceptionHandler);
#endif

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

    [DllImport("ntdll.dll", EntryPoint = "wine_get_version")]
    private static extern IntPtr WineGetVersion();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern bool TerminateProcess(IntPtr hProcess, uint exitCode);

    private static bool IsRunningUnderWine()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            WineGetVersion();
            return true;
        }
        catch
        {
            // wine_get_version doesn't exist on real Windows
            return false;
        }
    }

    private static bool TryRelaunchWithoutWxorX(string[] args)
    {
        try
        {
            if (Environment.GetEnvironmentVariable("DOTNET_EnableWriteXorExecute") != null)
                return false;

            if (!IsRunningUnderWine())
                return false;

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(processPath))
                return false;

            var psi = new ProcessStartInfo(processPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Environment.CurrentDirectory
            };

            // If launched through "dotnet UABEANext4.Desktop.dll", pass the dll path along.
            if (Path.GetFileNameWithoutExtension(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            {
                var entry = Environment.GetCommandLineArgs()[0];
                psi.ArgumentList.Add(entry);
            }

            foreach (var arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            psi.Environment["DOTNET_EnableWriteXorExecute"] = "0";

            var child = Process.Start(psi);
            if (child == null)
                return false;

            // The child is running on its own now. Hard-terminate this process instead of
            // Environment.Exit: Exit runs the managed shutdown inside the W^X-enabled runtime,
            // which can stall under Wine and leave this instance (and its memory) alive.
            if (!TerminateProcess(GetCurrentProcess(), 0))
                Environment.Exit(0);
            return true;
        }
        catch
        {
            // If anything goes wrong, just continue normally in this process.
            return false;
        }
    }

    public static void UABEAExceptionHandler(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception ex)
        {
            File.WriteAllText("uabeacrash.log", ex.ToString());
            if (OperatingSystem.IsWindows())
            {
                // can't trust the process to be stable enough
                // to even show an avalonia messagebox, do hacky
                // vbscript instead
                var mshtaArgs = "vbscript:Execute(\"CreateObject(\"\"WScript.Shell\"\").Popup CreateObject(\"\"Scripting.FileSystemObject\"\").OpenTextFile(\"\"uabeacrash.log\"\", 1).ReadAll,,\"\"uabea crash exception (please report this crash with uabeacrash.log)\"\" :close\")";
                Process.Start(new ProcessStartInfo("mshta", mshtaArgs));
            }
            else
            {
                Console.WriteLine("uabea crash exception (please report this crash with uabeacrash.log)");
                Console.WriteLine(ex.ToString());
            }
        }
    }

}
