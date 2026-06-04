namespace Ser2Net.Windows.Core;

public static class MappingResolver
{
    public static List<ResolvedMapping> Resolve(IEnumerable<PortMapping> mappings, IEnumerable<SerialDevice> devices)
    {
        var deviceList = devices.ToList();
        var resolved = new List<ResolvedMapping>();

        foreach (var mapping in mappings.Where(m => m.Enabled)) {
            var device = deviceList.FirstOrDefault(d => IsMatch(mapping.Match, d));
            if (device is not null) {
                resolved.Add(new ResolvedMapping(mapping, device));
            }
        }

        return resolved;
    }

    private static bool IsMatch(DeviceMatch match, SerialDevice device)
    {
        return match.Mode.ToLowerInvariant() switch
        {
            "usb-serial" => Same(match.Vid, device.Vid) &&
                            Same(match.Pid, device.Pid) &&
                            Same(match.SerialNumber, device.UsbSerial) &&
                            InterfaceMatches(match.Interface, device),
            "usb-location" => Same(match.LocationPath, device.LocationPath) &&
                              OptionalSame(match.Vid, device.Vid) &&
                              OptionalSame(match.Pid, device.Pid) &&
                              InterfaceMatches(match.Interface, device),
            "com-name" => Same(match.PortName, device.PortName),
            _ => false
        };
    }

    private static bool Same(string expected, string actual) =>
        !string.IsNullOrWhiteSpace(expected) &&
        string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool OptionalSame(string expected, string actual) =>
        string.IsNullOrWhiteSpace(expected) || Same(expected, actual);

    private static bool InterfaceMatches(string expected, SerialDevice device) =>
        string.IsNullOrWhiteSpace(expected) ||
        device.DeviceInstanceId.Contains(expected, StringComparison.OrdinalIgnoreCase) ||
        device.HardwareIds.Any(id => id.Contains(expected, StringComparison.OrdinalIgnoreCase));
}
