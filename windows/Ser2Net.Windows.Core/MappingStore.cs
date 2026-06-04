using System.Text.Json;

namespace Ser2Net.Windows.Core;

public static class MappingStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static MappingFile LoadOrCreate(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if (!File.Exists(path)) {
            var sample = CreateSample();
            Save(path, sample);
            return sample;
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MappingFile>(json, JsonOptions) ?? new MappingFile();
    }

    public static void Save(string path, MappingFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, JsonOptions));
    }

    public static MappingFile CreateSample() => new()
    {
        Mappings =
        [
            new PortMapping
            {
                Name = "sample-com4",
                Enabled = false,
                Match = new DeviceMatch { Mode = "com-name", PortName = "COM4" },
                Tcp = new TcpEndpoint { Mode = "telnet", ListenAddress = "0.0.0.0", Port = 3001, MaxConnections = 10 },
                Serial = new SerialSettings { Baud = 115200, Settings = "N81" }
            }
        ]
    };
}
