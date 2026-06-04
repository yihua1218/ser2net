using System.Diagnostics;
using System.Text.Json;
using Ser2Net.Windows.Core;

namespace Ser2Net.Service;

internal static class Program
{
    private const string ServiceName = "Ser2Net";

    public static int Main(string[] args)
    {
        try {
            if (args.Length > 0) {
                return RunCommand(args);
            }

            return WindowsServiceHost.Run(ServiceName, new Ser2NetDaemon(Ser2NetPaths.FromExecutable()));
        }
        catch (Exception ex) {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunCommand(string[] args)
    {
        var command = args[0].ToLowerInvariant();
        var paths = GetPaths(args);

        return command switch
        {
            "list-devices" => ListDevices(),
            "generate" => Generate(paths),
            "run-console" => new Ser2NetDaemon(paths).RunConsole(),
            "install" => ServiceInstaller.Install(ServiceName, Environment.ProcessPath ?? "Ser2Net.Service.exe"),
            "uninstall" => ServiceInstaller.Uninstall(ServiceName),
            "start" => ServiceInstaller.Control("start", ServiceName),
            "stop" => ServiceInstaller.Control("stop", ServiceName),
            "restart" => Restart(),
            _ => Usage()
        };
    }

    private static Ser2NetPaths GetPaths(string[] args)
    {
        for (var i = 0; i < args.Length - 1; i++) {
            if (args[i].Equals("--root", StringComparison.OrdinalIgnoreCase)) {
                return Ser2NetPaths.FromRoot(Path.GetFullPath(args[i + 1]));
            }
        }

        return Ser2NetPaths.FromExecutable();
    }

    private static int ListDevices()
    {
        var devices = SerialPortEnumerator.Enumerate();
        Console.WriteLine(JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int Generate(Ser2NetPaths paths)
    {
        var mappings = MappingStore.LoadOrCreate(paths.MappingFilePath);
        var resolved = MappingResolver.Resolve(mappings.Mappings, SerialPortEnumerator.Enumerate());
        Ser2NetConfigGenerator.WriteConfig(paths.GeneratedConfigPath, resolved);
        Console.WriteLine($"Generated {paths.GeneratedConfigPath} with {resolved.Count} resolved mapping(s).");
        return 0;
    }

    private static int Restart()
    {
        ServiceInstaller.Control("stop", ServiceName);
        Thread.Sleep(1000);
        return ServiceInstaller.Control("start", ServiceName);
    }

    private static int Usage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Ser2Net.Service.exe list-devices");
        Console.WriteLine("  Ser2Net.Service.exe generate [--root <Ser2Net root>]");
        Console.WriteLine("  Ser2Net.Service.exe run-console [--root <Ser2Net root>]");
        Console.WriteLine("  Ser2Net.Service.exe install|uninstall|start|stop|restart");
        return 2;
    }
}

internal sealed class Ser2NetDaemon
{
    private readonly Ser2NetPaths _paths;
    private readonly CancellationTokenSource _stop = new();
    private Process? _child;

    public Ser2NetDaemon(Ser2NetPaths paths)
    {
        _paths = paths;
    }

    public void Stop()
    {
        _stop.Cancel();
        StopChild();
    }

    public int RunConsole()
    {
        Run(_stop.Token);
        return 0;
    }

    public void Run(CancellationToken token)
    {
        Directory.CreateDirectory(_paths.LogDirectory);
        if (GenerateConfig() > 0) {
            StartChild();
        }

        while (!token.IsCancellationRequested && !_stop.IsCancellationRequested) {
            if (_child is { HasExited: true }) {
                Log($"ser2net.exe exited with code {_child.ExitCode}; restarting.");
                if (GenerateConfig() > 0) {
                    StartChild();
                }
            }

            token.WaitHandle.WaitOne(TimeSpan.FromSeconds(2));
        }

        StopChild();
    }

    private int GenerateConfig()
    {
        var mappings = MappingStore.LoadOrCreate(_paths.MappingFilePath);
        var devices = SerialPortEnumerator.Enumerate();
        var resolved = MappingResolver.Resolve(mappings.Mappings, devices);
        Ser2NetConfigGenerator.WriteConfig(_paths.GeneratedConfigPath, resolved);
        Log($"Generated config with {resolved.Count} resolved mapping(s).");
        if (resolved.Count == 0) {
            Log("No resolved mappings are present; ser2net.exe will not be started.");
        }

        return resolved.Count;
    }

    private void StartChild()
    {
        StopChild();

        if (!File.Exists(_paths.Ser2NetExePath)) {
            Log($"ser2net.exe not found: {_paths.Ser2NetExePath}");
            return;
        }

        var logPath = Path.Combine(_paths.LogDirectory, "ser2net.log");
        var startInfo = new ProcessStartInfo
        {
            FileName = _paths.Ser2NetExePath,
            Arguments = $"-n -c \"{_paths.GeneratedConfigPath}\"",
            WorkingDirectory = _paths.BinDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        _child = Process.Start(startInfo);
        if (_child is null) {
            Log("Failed to start ser2net.exe.");
            return;
        }

        _child.OutputDataReceived += (_, e) => AppendLine(logPath, e.Data);
        _child.ErrorDataReceived += (_, e) => AppendLine(logPath, e.Data);
        _child.BeginOutputReadLine();
        _child.BeginErrorReadLine();
        Log($"Started ser2net.exe pid={_child.Id}.");
    }

    private void StopChild()
    {
        if (_child is null) {
            return;
        }

        try {
            if (!_child.HasExited) {
                _child.Kill(entireProcessTree: true);
                _child.WaitForExit(5000);
            }
        }
        catch (Exception ex) {
            Log($"Failed to stop ser2net.exe: {ex.Message}");
        }
        finally {
            _child.Dispose();
            _child = null;
        }
    }

    private void Log(string message) => AppendLine(Path.Combine(_paths.LogDirectory, "service.log"), $"[{DateTimeOffset.Now:O}] {message}");

    private static void AppendLine(string path, string? line)
    {
        if (string.IsNullOrEmpty(line)) {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.AppendAllText(path, line + Environment.NewLine);
    }
}
