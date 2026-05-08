namespace Flowarden.Ui.Configuration;

public sealed class CoreEndpointOptions
{
    public CoreEndpointOptions(string bindAddress, string source)
    {
        BindAddress = bindAddress;
        Source = source;
    }

    public string BindAddress { get; }

    public string Source { get; }

    public string GrpcAddress => $"http://{BindAddress}";
}
