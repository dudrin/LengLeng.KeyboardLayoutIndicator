namespace LengLeng.KeyboardLayoutIndicator;

internal interface IServiceRuntime
{
    void Run(CancellationToken cancellationToken);

    void RequestScan();
}
