namespace Tickbird.Core.Charting;

/// <summary>
/// Largest-Triangle-Three-Buckets downsampling. It keeps the points that matter visually (peaks, dips, first and
/// last), so a 1,000,000-row result looks the same as the full data when drawn a few thousand pixels wide.
/// Gaps (NaN values) are preserved: each run of real values is reduced on its own and the NaN separators are kept.
/// </summary>
public static class Downsampler
{
    /// <summary>Reduces sorted <paramref name="xs"/>/<paramref name="ys"/> to about <paramref name="threshold"/> points.</summary>
    public static (double[] Xs, double[] Ys) Lttb(double[] xs, double[] ys, int threshold)
    {
        if (xs.Length != ys.Length) throw new ArgumentException("X and Y must be the same length.");
        if (threshold < 3 || xs.Length <= threshold) return (xs, ys);

        // Find runs of non-NaN values.
        var runs = new List<(int Start, int Length)>();
        var start = -1;
        for (var i = 0; i <= ys.Length; i++)
        {
            var real = i < ys.Length && !double.IsNaN(ys[i]);
            if (real && start < 0) start = i;
            else if (!real && start >= 0) { runs.Add((start, i - start)); start = -1; }
        }
        var realPoints = runs.Sum(r => r.Length);
        if (realPoints == 0) return (Array.Empty<double>(), Array.Empty<double>());

        // Separators use some of the budget; keep at least 3 points per run where possible.
        var budget = Math.Max(threshold - (runs.Count - 1), runs.Count * 2);
        var outX = new List<double>(threshold + runs.Count);
        var outY = new List<double>(threshold + runs.Count);
        for (var r = 0; r < runs.Count; r++)
        {
            var (runStart, length) = runs[r];
            if (r > 0)
            {
                // A gap marker between runs, so the chart still breaks the line here.
                var prevEnd = runs[r - 1].Start + runs[r - 1].Length - 1;
                outX.Add((xs[prevEnd] + xs[runStart]) / 2);
                outY.Add(double.NaN);
            }
            var target = (int)Math.Max(2, Math.Round((double)budget * length / realPoints));
            if (length <= target || target < 3)
            {
                if (length <= target)
                {
                    for (var i = runStart; i < runStart + length; i++) { outX.Add(xs[i]); outY.Add(ys[i]); }
                }
                else
                {
                    outX.Add(xs[runStart]); outY.Add(ys[runStart]);
                    outX.Add(xs[runStart + length - 1]); outY.Add(ys[runStart + length - 1]);
                }
                continue;
            }
            LttbRun(xs, ys, runStart, length, target, outX, outY);
        }
        return (outX.ToArray(), outY.ToArray());
    }

    private static void LttbRun(double[] xs, double[] ys, int start, int length, int threshold, List<double> outX, List<double> outY)
    {
        var end = start + length;           // exclusive
        var bucketSize = (double)(length - 2) / (threshold - 2);
        var a = start;
        outX.Add(xs[a]); outY.Add(ys[a]);

        for (var bucket = 0; bucket < threshold - 2; bucket++)
        {
            // Average of the next bucket, used as the third triangle corner.
            var nextStart = start + (int)Math.Floor((bucket + 1) * bucketSize) + 1;
            var nextEnd = Math.Min(start + (int)Math.Floor((bucket + 2) * bucketSize) + 1, end);
            if (nextStart >= end) nextStart = end - 1;
            if (nextEnd <= nextStart) nextEnd = nextStart + 1;
            double avgX = 0, avgY = 0;
            for (var i = nextStart; i < nextEnd; i++) { avgX += xs[i]; avgY += ys[i]; }
            var count = nextEnd - nextStart;
            avgX /= count; avgY /= count;

            // Pick the point in this bucket forming the largest triangle with the previous pick and the average.
            var rangeStart = start + (int)Math.Floor(bucket * bucketSize) + 1;
            var rangeEnd = Math.Min(start + (int)Math.Floor((bucket + 1) * bucketSize) + 1, end - 1);
            var best = rangeStart;
            var maxArea = -1.0;
            for (var i = rangeStart; i < rangeEnd; i++)
            {
                var area = Math.Abs((xs[a] - avgX) * (ys[i] - ys[a]) - (xs[a] - xs[i]) * (avgY - ys[a]));
                if (area > maxArea) { maxArea = area; best = i; }
            }
            outX.Add(xs[best]); outY.Add(ys[best]);
            a = best;
        }

        outX.Add(xs[end - 1]); outY.Add(ys[end - 1]);
    }
}
