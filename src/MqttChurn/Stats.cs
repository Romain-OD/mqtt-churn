using System.Globalization;
using System.Text;

static class Stats
{
    // Nearest-rank percentile: the smallest value with at least p% of samples at or below it.
    public static double Percentile(IReadOnlyList<double> sorted, double p)
    {
        if (sorted.Count == 0) throw new ArgumentException("No samples.", nameof(sorted));
        var rank = (int)Math.Ceiling(p / 100 * sorted.Count);
        return sorted[Math.Clamp(rank, 1, sorted.Count) - 1];
    }

    public static string Histogram(IReadOnlyList<double> sorted, int buckets = 10, int width = 40)
    {
        if (sorted.Count == 0) return "";
        double min = sorted[0], max = sorted[^1], size = Math.Max((max - min) / buckets, 1e-9);
        var counts = new int[buckets];
        foreach (var v in sorted) counts[Math.Min((int)((v - min) / size), buckets - 1)]++;

        var top = counts.Max();
        var text = new StringBuilder();
        for (var b = 0; b < buckets; b++)
        {
            var bar = new string('#', (int)Math.Round((double)counts[b] / top * width));
            text.AppendLine(CultureInfo.InvariantCulture, $"{min + b * size,8:0.0} ms | {bar} {counts[b]}");
        }
        return text.ToString();
    }

    public static string Report(IReadOnlyList<ClientResult> results)
    {
        var ok = results.Where(r => r.Error is null).Select(r => r.ConnectMs).Order().ToArray();
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"connected {ok.Length}/{results.Count}");
        foreach (var group in results.Where(r => r.Error is not null).GroupBy(r => r.Error))
            text.AppendLine(CultureInfo.InvariantCulture, $"  failed  {group.Count()} x {group.Key}");
        if (ok.Length == 0) return text.ToString();

        text.AppendLine(CultureInfo.InvariantCulture,
            $"connect+online ms  p50 {Percentile(ok, 50):0.0}  p95 {Percentile(ok, 95):0.0}  p99 {Percentile(ok, 99):0.0}  max {ok[^1]:0.0}");
        text.Append(Histogram(ok));
        return text.ToString();
    }

    public static string Csv(IEnumerable<ClientResult> results) =>
        "client,start_ms,connect_ms,error\n" + string.Concat(results.Select(r =>
            string.Create(CultureInfo.InvariantCulture, $"{r.Index},{r.StartMs:0.0},{r.ConnectMs:0.0},\"{r.Error?.Replace("\"", "'")}\"\n")));
}
