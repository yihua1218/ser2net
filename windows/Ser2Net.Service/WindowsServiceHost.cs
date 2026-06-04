using System.Runtime.InteropServices;

namespace Ser2Net.Service;

internal static class WindowsServiceHost
{
    private static Ser2NetDaemon? _daemon;
    private static IntPtr _statusHandle;
    private static HandlerEx? _handler;

    public static int Run(string serviceName, Ser2NetDaemon daemon)
    {
        _daemon = daemon;
        _handler = ServiceControlHandler;
        var table = new ServiceTableEntry[2];
        table[0] = new ServiceTableEntry { ServiceName = serviceName, ServiceProc = ServiceMainProc };
        table[1] = new ServiceTableEntry { ServiceName = null, ServiceProc = null };

        return StartServiceCtrlDispatcher(table) ? 0 : 1;
    }

    private static void ServiceMainProc(uint argc, IntPtr argv)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx("Ser2Net", _handler!, IntPtr.Zero);
        SetStatus(ServiceState.StartPending);
        SetStatus(ServiceState.Running, acceptedControls: ServiceAcceptStop | ServiceAcceptShutdown);

        try {
            _daemon!.Run(CancellationToken.None);
        }
        finally {
            SetStatus(ServiceState.Stopped);
        }
    }

    private static uint ServiceControlHandler(uint control, uint eventType, IntPtr eventData, IntPtr context)
    {
        if (control is ServiceControlStop or ServiceControlShutdown) {
            SetStatus(ServiceState.StopPending);
            _daemon?.Stop();
            SetStatus(ServiceState.Stopped);
        }

        return 0;
    }

    private static void SetStatus(ServiceState state, uint acceptedControls = 0)
    {
        if (_statusHandle == IntPtr.Zero) {
            return;
        }

        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = (uint)state,
            ControlsAccepted = acceptedControls,
            Win32ExitCode = 0,
            ServiceSpecificExitCode = 0,
            CheckPoint = 0,
            WaitHint = 0
        };
        SetServiceStatus(_statusHandle, ref status);
    }

    private const uint ServiceWin32OwnProcess = 0x00000010;
    private const uint ServiceAcceptStop = 0x00000001;
    private const uint ServiceAcceptShutdown = 0x00000004;
    private const uint ServiceControlStop = 0x00000001;
    private const uint ServiceControlShutdown = 0x00000005;

    private enum ServiceState : uint
    {
        Stopped = 1,
        StartPending = 2,
        StopPending = 3,
        Running = 4
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public uint ServiceType;
        public uint CurrentState;
        public uint ControlsAccepted;
        public uint Win32ExitCode;
        public uint ServiceSpecificExitCode;
        public uint CheckPoint;
        public uint WaitHint;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        public string? ServiceName;
        public ServiceMainDelegate? ServiceProc;
    }

    private delegate void ServiceMainDelegate(uint argc, IntPtr argv);
    private delegate uint HandlerEx(uint control, uint eventType, IntPtr eventData, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceCtrlDispatcher([In] ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr RegisterServiceCtrlHandlerEx(string serviceName, HandlerEx handlerProc, IntPtr context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(IntPtr serviceStatusHandle, ref ServiceStatus serviceStatus);
}
