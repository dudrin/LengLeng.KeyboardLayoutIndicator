using System.Runtime.InteropServices;

namespace LengLeng.KeyboardLayoutIndicator;

internal static class WindowsServiceHost
{
    private const int ErrorFailedServiceControllerConnect = 1063;
    private const int ServiceWin32OwnProcess = 0x00000010;
    private const int ServiceStopped = 0x00000001;
    private const int ServiceStartPending = 0x00000002;
    private const int ServiceStopPending = 0x00000003;
    private const int ServiceRunning = 0x00000004;
    private const int ServiceControlStop = 0x00000001;
    private const int ServiceControlShutdown = 0x00000005;
    private const int ServiceControlSessionChange = 0x0000000E;
    private const int ServiceControlInterrogate = 0x00000004;
    private const int ServiceAcceptStop = 0x00000001;
    private const int ServiceAcceptShutdown = 0x00000004;
    private const int ServiceAcceptSessionChange = 0x00000080;

    private static readonly ServiceMainDelegate ServiceMainCallback = ServiceMain;
    private static readonly ServiceControlHandlerExDelegate ControlHandlerCallback = ControlHandler;
    private static readonly ManualResetEventSlim Stopped = new(false);

    private static IServiceRuntime? _runtime;
    private static CancellationTokenSource? _cancellation;
    private static nint _statusHandle;
    private static int _currentState = ServiceStopped;

    public static int Run(IServiceRuntime runtime)
    {
        _runtime = runtime;

        var serviceTable = new[]
        {
            new ServiceTableEntry
            {
                ServiceName = Program.ServiceName,
                ServiceMain = ServiceMainCallback
            },
            new ServiceTableEntry()
        };

        if (StartServiceCtrlDispatcher(serviceTable))
        {
            return 0;
        }

        var error = Marshal.GetLastWin32Error();
        if (error == ErrorFailedServiceControllerConnect)
        {
            Console.Error.WriteLine(
                "This executable is a Windows service. Use installer\\install.ps1 or run with --run-service-console for diagnostics.");
        }
        else
        {
            Console.Error.WriteLine($"StartServiceCtrlDispatcher failed with Win32 error {error}.");
            FileLog.Write("service", $"StartServiceCtrlDispatcher failed with Win32 error {error}.");
        }

        return 1;
    }

    private static void ServiceMain(int argumentCount, nint arguments)
    {
        _statusHandle = RegisterServiceCtrlHandlerEx(
            Program.ServiceName,
            ControlHandlerCallback,
            0);

        if (_statusHandle == 0)
        {
            return;
        }

        SetServiceState(ServiceStartPending, acceptedControls: 0, waitHint: 3000);

        _cancellation = new CancellationTokenSource();

        _ = Task.Run(() =>
        {
            try
            {
                _runtime?.Run(_cancellation.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                FileLog.Write("service", "Service runtime failed.", ex);
            }
            finally
            {
                SetServiceState(ServiceStopped, acceptedControls: 0, waitHint: 0);
                Stopped.Set();
            }
        });

        SetServiceState(
            ServiceRunning,
            ServiceAcceptStop | ServiceAcceptShutdown | ServiceAcceptSessionChange,
            waitHint: 0);

        Stopped.Wait();
    }

    private static int ControlHandler(int control, int eventType, nint eventData, nint context)
    {
        switch (control)
        {
            case ServiceControlStop:
            case ServiceControlShutdown:
                SetServiceState(
                    ServiceStopPending,
                    ServiceAcceptStop | ServiceAcceptShutdown | ServiceAcceptSessionChange,
                    waitHint: 5000);
                _cancellation?.Cancel();
                return 0;

            case ServiceControlSessionChange:
                _runtime?.RequestScan();
                return 0;

            case ServiceControlInterrogate:
                SetServiceState(
                    _currentState,
                    ServiceAcceptStop | ServiceAcceptShutdown | ServiceAcceptSessionChange,
                    waitHint: 0);
                return 0;

            default:
                return 0;
        }
    }

    private static void SetServiceState(int state, int acceptedControls, int waitHint)
    {
        _currentState = state;
        var status = new ServiceStatus
        {
            ServiceType = ServiceWin32OwnProcess,
            CurrentState = state,
            ControlsAccepted = acceptedControls,
            Win32ExitCode = 0,
            ServiceSpecificExitCode = 0,
            CheckPoint = state is ServiceRunning or ServiceStopped ? 0 : 1,
            WaitHint = waitHint
        };

        SetServiceStatus(_statusHandle, ref status);
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool StartServiceCtrlDispatcher(ServiceTableEntry[] serviceTable);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint RegisterServiceCtrlHandlerEx(
        string serviceName,
        ServiceControlHandlerExDelegate handler,
        nint context);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool SetServiceStatus(nint serviceStatusHandle, ref ServiceStatus serviceStatus);

    private delegate void ServiceMainDelegate(int argumentCount, nint arguments);

    private delegate int ServiceControlHandlerExDelegate(
        int control,
        int eventType,
        nint eventData,
        nint context);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ServiceTableEntry
    {
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? ServiceName;
        public ServiceMainDelegate? ServiceMain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ServiceStatus
    {
        public int ServiceType;
        public int CurrentState;
        public int ControlsAccepted;
        public int Win32ExitCode;
        public int ServiceSpecificExitCode;
        public int CheckPoint;
        public int WaitHint;
    }
}
