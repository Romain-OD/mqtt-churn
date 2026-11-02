using MQTTnet;
using MQTTnet.Protocol;

var builder = WebApplication.CreateBuilder(args);

var config = builder.Configuration;
var runId = config["run-id"] ?? "demo";
var grid = new Grid(config.GetValue("clients", 1000));

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/frames", (CancellationToken ct) =>
    TypedResults.ServerSentEvents(grid.Frames(ct), eventType: "frame"));

// The watcher is just another subscriber: the retained status of every client first, then each change.
using var mqtt = new MqttClientFactory().CreateMqttClient();
mqtt.ApplicationMessageReceivedAsync += e =>
{
    grid.Apply(e.ApplicationMessage.Topic, e.ApplicationMessage.ConvertPayloadToString());
    return Task.CompletedTask;
};
await mqtt.ConnectAsync(new MqttClientOptionsBuilder()
    .WithClientId($"churn-{runId}-watch") // named in the broker log; a 2nd watcher on this run-id takes over
    .WithTcpServer(config["broker"] ?? "127.0.0.1", config.GetValue("port", 1883))
    .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
    .Build());
await mqtt.SubscribeAsync($"churn/{runId}/+/status", MqttQualityOfServiceLevel.AtLeastOnce);

await app.RunAsync();
