using Flowarden.Discovery.V1;
using Flowarden.Health.V1;
using Grpc.Net.Client;

var address = args.Length > 0 ? args[0] : "http://127.0.0.1:39091";
using var channel = GrpcChannel.ForAddress(address);

var healthClient = new HealthService.HealthServiceClient(channel);
var discoveryClient = new DiscoveryService.DiscoveryServiceClient(channel);

var health = await healthClient.GetHealthAsync(new GetHealthRequest());
var version = await healthClient.GetVersionAsync(new GetVersionRequest());
var devices = await discoveryClient.ListDevicesAsync(new ListDevicesRequest());

Console.WriteLine($"health.status={health.Status}");
Console.WriteLine($"version.service={version.Service}");
Console.WriteLine($"version.version={version.Version}");
Console.WriteLine($"devices.count={devices.Devices.Count}");
