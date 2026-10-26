using System.Globalization;
using System.Numerics;

sealed record Options(
    string Broker,
    int Port,
    int Clients,
    double Rate,
    ushort KeepAlive,
    TimeSpan Hold,
    string RunId,
    string? Csv)
{
    public const string Usage = """
        mqtt-churn [--broker 127.0.0.1] [--port 1883] [--clients 100] [--rate 50]
                   [--keepalive 5] [--hold 10] [--run-id <random>] [--csv <file>]
        """;

    static readonly string[] Known =
        ["--broker", "--port", "--clients", "--rate", "--keepalive", "--hold", "--run-id", "--csv"];

    public static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>();
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!Known.Contains(args[i])) throw new ArgumentException($"Unknown option '{args[i]}'.");
            if (i + 1 >= args.Length) throw new ArgumentException($"Option '{args[i]}' needs a value.");
            values[args[i]] = args[i + 1];
        }

        string Get(string name, string fallback) => values.GetValueOrDefault(name, fallback);

        T Num<T>(string name, string fallback, T min) where T : INumber<T>
        {
            var raw = Get(name, fallback);
            if (!T.TryParse(raw, CultureInfo.InvariantCulture, out var value) || value < min)
                throw new ArgumentException($"Option '{name}' must be a number >= {min}, got '{raw}'.");
            return value;
        }

        return new Options(
            Broker: Get("--broker", "127.0.0.1"),
            Port: Num("--port", "1883", 1),
            Clients: Num("--clients", "100", 1),
            Rate: Num("--rate", "50", 0.1),
            KeepAlive: Num<ushort>("--keepalive", "5", 1),
            Hold: TimeSpan.FromSeconds(Num("--hold", "10", 0.0)),
            RunId: Get("--run-id", Guid.NewGuid().ToString("N")[..6]),
            Csv: values.GetValueOrDefault("--csv"));
    }
}
