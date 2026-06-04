using System.Text.Json.Serialization;

namespace Ser2Net.Windows.Core;

public sealed class MappingFile
{
    [JsonPropertyName("mappings")]
    public List<PortMapping> Mappings { get; set; } = [];
}

public sealed class PortMapping
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("match")]
    public DeviceMatch Match { get; set; } = new();

    [JsonPropertyName("tcp")]
    public TcpEndpoint Tcp { get; set; } = new();

    [JsonPropertyName("serial")]
    public SerialSettings Serial { get; set; } = new();

    [JsonPropertyName("banner")]
    public string Banner { get; set; } = "";
}

public sealed class DeviceMatch
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "com-name";

    [JsonPropertyName("portName")]
    public string PortName { get; set; } = "";

    [JsonPropertyName("vid")]
    public string Vid { get; set; } = "";

    [JsonPropertyName("pid")]
    public string Pid { get; set; } = "";

    [JsonPropertyName("serialNumber")]
    public string SerialNumber { get; set; } = "";

    [JsonPropertyName("locationPath")]
    public string LocationPath { get; set; } = "";

    [JsonPropertyName("interface")]
    public string Interface { get; set; } = "";
}

public sealed class TcpEndpoint
{
    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "telnet";

    [JsonPropertyName("listenAddress")]
    public string ListenAddress { get; set; } = "0.0.0.0";

    [JsonPropertyName("port")]
    public int Port { get; set; } = 3001;

    [JsonPropertyName("maxConnections")]
    public int MaxConnections { get; set; } = 10;
}

public sealed class SerialSettings
{
    [JsonPropertyName("baud")]
    public int Baud { get; set; } = 115200;

    [JsonPropertyName("settings")]
    public string Settings { get; set; } = "N81";

    [JsonPropertyName("options")]
    public string[] Options { get; set; } = [];
}

public sealed record ResolvedMapping(PortMapping Mapping, SerialDevice Device);
