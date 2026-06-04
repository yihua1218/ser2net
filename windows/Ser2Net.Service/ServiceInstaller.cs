using System.Diagnostics;

namespace Ser2Net.Service;

internal static class ServiceInstaller
{
    public static int Install(string serviceName, string executablePath)
    {
        var fullPath = Path.GetFullPath(executablePath);
        var createResult = RunSc("create", serviceName, "binPath=", fullPath, "start=", "auto", "DisplayName=", "Ser2Net");
        return createResult == ServiceAlreadyExists
            ? RunSc("config", serviceName, "binPath=", fullPath, "start=", "auto", "DisplayName=", "Ser2Net")
            : createResult;
    }

    public static int Uninstall(string serviceName)
    {
        Control("stop", serviceName);
        return RunSc("delete", serviceName);
    }

    public static int Control(string action, string serviceName)
    {
        var result = RunSc(action, serviceName);
        if (action.Equals("start", StringComparison.OrdinalIgnoreCase) && result == ServiceAlreadyRunning) {
            return 0;
        }

        if (action.Equals("stop", StringComparison.OrdinalIgnoreCase) && result == ServiceNotActive) {
            return 0;
        }

        return result;
    }

    private static int RunSc(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "sc.exe",
            UseShellExecute = false
        };

        foreach (var arg in args) {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi);
        process?.WaitForExit();
        return process?.ExitCode ?? 1;
    }

    private const int ServiceAlreadyExists = 1073;
    private const int ServiceAlreadyRunning = 1056;
    private const int ServiceNotActive = 1062;
}
