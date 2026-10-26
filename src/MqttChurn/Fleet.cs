using System.Diagnostics;
using MQTTnet;
using MQTTnet.Protocol;

sealed record ClientResult(int Index, double StartMs, double ConnectMs, string? Error);

static class Fleet
{
    public static string Topic(string runId, int i) => $"churn/{runId}/{i}/status";

    // Client i starts at i / rate seconds: a steady arrival rate, not a burst.
    public static TimeSpan StartAt(int i, double rate) => TimeSpan.FromSeconds(i / rate);

    public static async Task<ClientResult[]> RunAsync(Options o, CancellationToken ct)
    {
        var factory = new MqttClientFactory();
        var clients = new IMqttClient?[o.Clients];
        var clock = Stopwatch.StartNew();

        var connects = Enumerable.Range(0, o.Clients).Select(async i =>
        {
            var wait = StartAt(i, o.Rate) - clock.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);

            var startMs = clock.Elapsed.TotalMilliseconds;
            var client = factory.CreateMqttClient();
            var topic = Topic(o.RunId, i);
            var options = new MqttClientOptionsBuilder()
                .WithTcpServer(o.Broker, o.Port)
                .WithClientId($"churn-{o.RunId}-{i}")
                .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V500)
                .WithKeepAlivePeriod(TimeSpan.FromSeconds(o.KeepAlive))
                .WithTimeout(TimeSpan.FromSeconds(10))
                .WithWillTopic(topic)
                .WithWillPayload("offline")
                .WithWillQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .WithWillRetain()
                .Build();
            try
            {
                await client.ConnectAsync(options, ct);
                await client.PublishStringAsync(topic, "online", MqttQualityOfServiceLevel.AtLeastOnce, retain: true, ct);
                clients[i] = client;
                return new ClientResult(i, startMs, clock.Elapsed.TotalMilliseconds - startMs, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                client.Dispose();
                var root = ex.GetBaseException();
                return new ClientResult(i, startMs, clock.Elapsed.TotalMilliseconds - startMs, $"{root.GetType().Name}: {root.Message}");
            }
        });
        var results = await Task.WhenAll(connects);

        Console.WriteLine($"{results.Count(r => r.Error is null)}/{o.Clients} online, holding {o.Hold.TotalSeconds:0.#}s...");
        await Task.Delay(o.Hold, ct);

        // Clean exit: clear the retained status (empty payload), then a normal DISCONNECT so no Will fires.
        await Task.WhenAll(clients.Select(async (client, i) =>
        {
            if (client is null) return;
            try
            {
                await client.PublishBinaryAsync(Topic(o.RunId, i), [], MqttQualityOfServiceLevel.AtLeastOnce, retain: true, CancellationToken.None);
                await client.DisconnectAsync(new MqttClientDisconnectOptions(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"client {i}: cleanup failed ({ex.GetType().Name})");
            }
            finally { client.Dispose(); }
        }));
        return results;
    }
}
