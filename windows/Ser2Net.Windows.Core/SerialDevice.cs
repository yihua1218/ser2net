using System.Text.RegularExpressions;

namespace Ser2Net.Windows.Core;

public sealed record SerialDevice
{
    public string PortName { get; init; } = "";
    public string FriendlyName { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string DeviceInstanceId { get; init; } = "";
    public string DevicePath { get; init; } = "";
    public string LocationPath { get; init; } = "";
    public string LocationInformation { get; init; } = "";
    public string[] HardwareIds { get; init; } = [];

    public string Vid => MatchUsbPart("VID_([0-9A-Fa-f]{4})");
    public string Pid => MatchUsbPart("PID_([0-9A-Fa-f]{4})");
    public string UsbSerial => ParseUsbSerial(DeviceInstanceId);

    private string MatchUsbPart(string pattern)
    {
        var source = DeviceInstanceId + "\n" + string.Join("\n", HardwareIds);
        var match = Regex.Match(source, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
    }

    private static string ParseUsbSerial(string instanceId)
    {
        var parts = instanceId.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3 || !parts[0].Equals("USB", StringComparison.OrdinalIgnoreCase)) {
            return "";
        }

        var value = parts[^1];
        var ampersand = value.IndexOf('&');
        return ampersand >= 0 ? value[..ampersand] : value;
    }
}
