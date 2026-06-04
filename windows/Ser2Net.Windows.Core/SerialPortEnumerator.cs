using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Ser2Net.Windows.Core;

public static partial class SerialPortEnumerator
{
    private static readonly Guid GuidDevInterfaceComPort = new("86E0D1E0-8089-11D0-9CE4-08003E301F73");

    public static IReadOnlyList<SerialDevice> Enumerate()
    {
        if (!OperatingSystem.IsWindows()) {
            return [];
        }

        var classGuid = GuidDevInterfaceComPort;
        var infoSet = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
        if (infoSet == InvalidHandleValue) {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try {
            var devices = new List<SerialDevice>();
            for (var index = 0u; ; index++) {
                var ifaceData = new SpDeviceInterfaceData { cbSize = Marshal.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref classGuid, index, ref ifaceData)) {
                    var err = Marshal.GetLastWin32Error();
                    if (err == ErrorNoMoreItems) {
                        break;
                    }

                    throw new Win32Exception(err);
                }

                devices.Add(ReadDevice(infoSet, ifaceData));
            }

            return devices
                .Where(d => !string.IsNullOrWhiteSpace(d.PortName))
                .OrderBy(d => PortSortKey(d.PortName))
                .ToList();
        }
        finally {
            SetupDiDestroyDeviceInfoList(infoSet);
        }
    }

    private static SerialDevice ReadDevice(IntPtr infoSet, SpDeviceInterfaceData ifaceData)
    {
        var devInfo = new SpDevInfoData { cbSize = Marshal.SizeOf<SpDevInfoData>() };
        SetupDiGetDeviceInterfaceDetail(infoSet, ref ifaceData, IntPtr.Zero, 0, out var requiredSize, ref devInfo);

        var detailPtr = Marshal.AllocHGlobal((int)requiredSize);
        try {
            Marshal.WriteInt32(detailPtr, IntPtr.Size == 8 ? 8 : 6);
            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref ifaceData, detailPtr, requiredSize, out _, ref devInfo)) {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var devicePath = Marshal.PtrToStringAuto(detailPtr + 4) ?? "";
            var friendlyName = GetProperty(infoSet, devInfo, SpdrpFriendlyName);
            var portName = GetPortName(infoSet, devInfo, friendlyName);

            return new SerialDevice
            {
                PortName = portName,
                FriendlyName = friendlyName,
                Manufacturer = GetProperty(infoSet, devInfo, SpdrpMfg),
                DeviceInstanceId = GetDeviceInstanceId(infoSet, devInfo),
                DevicePath = devicePath,
                LocationPath = GetMultiStringProperty(infoSet, devInfo, SpdrpLocationPaths).FirstOrDefault() ?? "",
                LocationInformation = GetProperty(infoSet, devInfo, SpdrpLocationInformation),
                HardwareIds = GetMultiStringProperty(infoSet, devInfo, SpdrpHardwareId)
            };
        }
        finally {
            Marshal.FreeHGlobal(detailPtr);
        }
    }

    private static string GetPortName(IntPtr infoSet, SpDevInfoData devInfo, string friendlyName)
    {
        var key = SetupDiOpenDevRegKey(infoSet, ref devInfo, DicsFlagGlobal, 0, DiregDev, KeyRead);
        if (key != IntPtr.Zero && key != InvalidHandleValue) {
            try {
                var value = QueryRegistryString(key, "PortName");
                if (!string.IsNullOrWhiteSpace(value)) {
                    return value;
                }
            }
            finally {
                RegCloseKey(key);
            }
        }

        var match = Regex.Match(friendlyName, @"\((COM\d+)\)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
    }

    private static string QueryRegistryString(IntPtr key, string name)
    {
        uint type;
        uint size = 512;
        var buffer = new byte[size];
        var result = RegQueryValueEx(key, name, IntPtr.Zero, out type, buffer, ref size);
        if (result != 0 || size == 0) {
            return "";
        }

        return Encoding.Unicode.GetString(buffer, 0, (int)size).TrimEnd('\0');
    }

    private static string GetProperty(IntPtr infoSet, SpDevInfoData devInfo, uint property) =>
        GetMultiStringProperty(infoSet, devInfo, property).FirstOrDefault() ?? "";

    private static string[] GetMultiStringProperty(IntPtr infoSet, SpDevInfoData devInfo, uint property)
    {
        var buffer = new byte[4096];
        if (!SetupDiGetDeviceRegistryProperty(infoSet, ref devInfo, property, out _, buffer, (uint)buffer.Length, out var required)) {
            return [];
        }

        var byteCount = (int)Math.Min(required, (uint)buffer.Length);
        var text = Encoding.Unicode.GetString(buffer, 0, byteCount).TrimEnd('\0');
        return text.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    private static string GetDeviceInstanceId(IntPtr infoSet, SpDevInfoData devInfo)
    {
        var sb = new StringBuilder(1024);
        return SetupDiGetDeviceInstanceId(infoSet, ref devInfo, sb, sb.Capacity, out _) ? sb.ToString() : "";
    }

    private static int PortSortKey(string portName)
    {
        var match = Regex.Match(portName, @"COM(\d+)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : int.MaxValue;
    }

    private const int ErrorNoMoreItems = 259;
    private const uint DigcfPresent = 0x00000002;
    private const uint DigcfDeviceInterface = 0x00000010;
    private const uint SpdrpHardwareId = 0x00000001;
    private const uint SpdrpMfg = 0x0000000B;
    private const uint SpdrpFriendlyName = 0x0000000C;
    private const uint SpdrpLocationInformation = 0x0000000D;
    private const uint SpdrpLocationPaths = 0x00000023;
    private const uint DicsFlagGlobal = 0x00000001;
    private const uint DiregDev = 0x00000001;
    private const uint KeyRead = 0x20019;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDeviceInterfaceData
    {
        public int cbSize;
        public Guid InterfaceClassGuid;
        public uint Flags;
        public UIntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevInfoData
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public UIntPtr Reserved;
    }

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SpDeviceInterfaceData deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SpDeviceInterfaceData deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, ref SpDevInfoData deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiOpenDevRegKey(IntPtr deviceInfoSet, ref SpDevInfoData deviceInfoData, uint scope, uint hwProfile, uint keyType, uint samDesired);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int RegQueryValueEx(IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, byte[] lpData, ref uint lpcbData);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegCloseKey(IntPtr hKey);
}
