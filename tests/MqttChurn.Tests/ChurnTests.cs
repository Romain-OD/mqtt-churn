namespace MqttChurn.Tests;

[TestClass]
public sealed class FleetTests
{
    [TestMethod]
    public void StartAt_SpacesClientsEvenlyAtTheRate()
    {
        Assert.AreEqual(TimeSpan.Zero, Fleet.StartAt(0, 50));
        Assert.AreEqual(TimeSpan.FromMilliseconds(20), Fleet.StartAt(1, 50));
        Assert.AreEqual(TimeSpan.FromSeconds(19.98), Fleet.StartAt(999, 50));
    }
}

[TestClass]
public sealed class StatsTests
{
    static readonly double[] OneToHundred = [.. Enumerable.Range(1, 100).Select(i => (double)i)];

    [TestMethod]
    [DataRow(50, 50)]
    [DataRow(95, 95)]
    [DataRow(99, 99)]
    [DataRow(100, 100)]
    [DataRow(0, 1)]
    public void Percentile_UsesNearestRank(double p, double expected) =>
        Assert.AreEqual(expected, Stats.Percentile(OneToHundred, p));

    [TestMethod]
    public void Percentile_SmallSampleRoundsUp() =>
        Assert.AreEqual(20.0, Stats.Percentile([10, 20, 30], 50));

    [TestMethod]
    public void Percentile_EmptyThrows() =>
        Assert.ThrowsExactly<ArgumentException>(() => Stats.Percentile([], 50));

    [TestMethod]
    public void Histogram_PutsEverySampleInABucket()
    {
        var lines = Stats.Histogram(OneToHundred, buckets: 4).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(4, lines);
        Assert.AreEqual(100, lines.Sum(l => int.Parse(l.Split(' ')[^1])));
    }

    [TestMethod]
    public void Histogram_SingleValueDoesNotDivideByZero() =>
        StringAssert.Contains(Stats.Histogram([5.0, 5.0], buckets: 3), "2");
}

[TestClass]
public sealed class OptionsTests
{
    [TestMethod]
    public void Parse_Defaults()
    {
        var o = Options.Parse([]);
        Assert.AreEqual(("127.0.0.1", 1883, 100, 50.0), (o.Broker, o.Port, o.Clients, o.Rate));
        Assert.HasCount(6, o.RunId);
    }

    [TestMethod]
    public void Parse_ReadsValuesWithInvariantCulture()
    {
        var o = Options.Parse(["--clients", "1000", "--rate", "12.5", "--hold", "0.5", "--run-id", "r1"]);
        Assert.AreEqual((1000, 12.5, TimeSpan.FromSeconds(0.5), "r1"), (o.Clients, o.Rate, o.Hold, o.RunId));
    }

    [TestMethod]
    [DataRow("--nope", "1")]
    [DataRow("--clients", "0")]
    [DataRow("--clients", "many")]
    [DataRow("--rate", "0")]
    [DataRow("--keepalive", "70000")]
    public void Parse_RejectsBadInput(string name, string value) =>
        Assert.ThrowsExactly<ArgumentException>(() => Options.Parse([name, value]));

    [TestMethod]
    public void Parse_RejectsMissingValue() =>
        Assert.ThrowsExactly<ArgumentException>(() => Options.Parse(["--clients"]));
}

[TestClass]
public sealed class GridTests
{
    [TestMethod]
    public void Index_ReadsTheClientNumberFromTheTopic()
    {
        Assert.AreEqual(42, Grid.Index("churn/demo/42/status"));
        Assert.IsNull(Grid.Index("churn/demo/status"));
        Assert.IsNull(Grid.Index("churn/demo/x/status"));
    }

    [TestMethod]
    [DataRow("online", 'o')]
    [DataRow("offline", 'x')]
    [DataRow("", '-')]
    [DataRow(null, '-')]
    public void Apply_MapsThePayloadToACellState(string? payload, char expected)
    {
        var grid = new Grid(3);
        grid.Apply("churn/demo/1/status", payload);
        Assert.AreEqual($".{expected}.", grid.Snapshot(1).Cells);
    }

    [TestMethod]
    public void Apply_IgnoresUnknownPayloadsAndClientsOutsideTheGrid()
    {
        var grid = new Grid(2);
        grid.Apply("churn/demo/0/status", "online");
        grid.Apply("churn/demo/0/status", "hello");
        grid.Apply("churn/demo/2/status", "offline");
        grid.Apply("churn/demo/-1/status", "offline");
        Assert.AreEqual("o.", grid.Snapshot(1).Cells);
    }

    [TestMethod]
    public void Snapshot_CountsEachState()
    {
        var grid = new Grid(5);
        grid.Apply("churn/demo/0/status", "online");
        grid.Apply("churn/demo/1/status", "online");
        grid.Apply("churn/demo/2/status", "offline");
        grid.Apply("churn/demo/3/status", "");
        Assert.AreEqual(new Frame(7, Online: 2, Offline: 1, Gone: 1, "oox-."), grid.Snapshot(7));
    }
}
