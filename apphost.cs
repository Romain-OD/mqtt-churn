#:sdk Aspire.AppHost.Sdk@13.5.4
#:property NoWarn=$(NoWarn);ASPIRE010

// `dotnet run apphost.cs`: the broker, the heatmap and a fleet you start/stop from the dashboard.
// Build the UI and publish both apps once first (README, "Run it with Aspire").
var builder = DistributedApplication.CreateBuilder(args);
const string clients = "5000";

var mosquitto = builder.AddContainer("mosquitto", "public.ecr.aws/docker/library/eclipse-mosquitto", "2")
    .WithBindMount("mosquitto/mosquitto.conf", "/mosquitto/config/mosquitto.conf", isReadOnly: true)
    .WithContainerRuntimeArgs("--ulimit", "nofile=65536:65536")
    .WithEndpoint(port: 1883, targetPort: 1883, name: "mqtt", scheme: "tcp");

// Containers next to the broker, as in Part 1: through the port forwarder the fleet stops at 1989
// and the kill wave spreads from 100 ms to about 1 s. Publish both first (see README).
var watch = builder.AddContainer("watch", "mcr.microsoft.com/dotnet/aspnet", "10.0")
    .WithBindMount("out/watch", "/app")
    .WithEntrypoint("dotnet")
    .WithArgs("/app/MqttChurn.Watch.dll", "--contentRoot", "/app", "--broker", "mosquitto", "--run-id", "demo", "--clients", clients)
    .WithHttpEndpoint(port: 5080, targetPort: 8080, isProxied: false)
    .WithOtlpExporter()
    .WaitFor(mosquitto);

// Stop in the dashboard drops every socket without a DISCONNECT, so the broker fires the Wills.
builder.AddContainer("fleet", "mcr.microsoft.com/dotnet/aspnet", "10.0")
    .WithBindMount("out/fleet", "/app")
    .WithEntrypoint("dotnet")
    .WithArgs("/app/MqttChurn.dll", "--broker", "mosquitto", "--clients", clients, "--rate", "500", "--hold", "600", "--run-id", "demo")
    .WithExplicitStart()
    .WaitFor(watch);

builder.Build().Run();
