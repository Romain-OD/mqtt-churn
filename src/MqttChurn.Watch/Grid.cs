using System.Runtime.CompilerServices;

// What the browser draws: the counts plus one char per client.
sealed record Frame(long Seq, int Online, int Offline, int Gone, string Cells);

// One cell per client index: '.' never seen, 'o' online, 'x' offline (its Will fired), '-' gone (clean exit).
sealed class Grid(int clients)
{
    readonly char[] cells = new string('.', clients).ToCharArray();

    public static int? Index(string topic) =>
        topic.Split('/') is [_, _, var i, _] && int.TryParse(i, out var n) ? n : null;

    public static char? State(string? payload) => payload switch
    {
        "online" => 'o',
        "offline" => 'x',
        null or "" => '-', // the clean exit clears the retained status
        _ => null,
    };

    public void Apply(string topic, string? payload)
    {
        if (Index(topic) is int i && i >= 0 && i < cells.Length && State(payload) is char state)
            cells[i] = state;
    }

    public Frame Snapshot(long seq)
    {
        var s = new string(cells);
        return new Frame(seq, s.AsSpan().Count('o'), s.AsSpan().Count('x'), s.AsSpan().Count('-'), s);
    }

    // Snapshots, not events: a late or reconnecting browser is right from its first frame.
    public async IAsyncEnumerable<Frame> Frames([EnumeratorCancellation] CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        for (long seq = 1; !ct.IsCancellationRequested; seq++)
        {
            yield return Snapshot(seq);
            await timer.WaitForNextTickAsync(ct);
        }
    }
}
