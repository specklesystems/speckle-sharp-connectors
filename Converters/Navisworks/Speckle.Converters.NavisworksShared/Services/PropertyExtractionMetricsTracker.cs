using System.Collections.Concurrent;

namespace Speckle.Converter.Navisworks.Services;

public static class PropertyExtractionMetricsTracker
{
  private static readonly ConcurrentBag<int> s_totalCategoryCounts = [];
  private static readonly ConcurrentBag<int> s_userFilteredCategoryCounts = [];
  private static readonly ConcurrentBag<int> s_propertyCounts = [];
  private static readonly ConcurrentBag<int> s_payloadBytes = [];
  private static readonly ConcurrentBag<double> s_elapsedMs = [];

  public static void Reset()
  {
    ClearBag(s_totalCategoryCounts);
    ClearBag(s_userFilteredCategoryCounts);
    ClearBag(s_propertyCounts);
    ClearBag(s_payloadBytes);
    ClearBag(s_elapsedMs);
  }

  public static void Record(
    int totalCategoryCount,
    int userFilteredCategoryCount,
    int propertyCount,
    int payloadBytes,
    double elapsedMs
  )
  {
    s_totalCategoryCounts.Add(totalCategoryCount);
    s_userFilteredCategoryCounts.Add(userFilteredCategoryCount);
    s_propertyCounts.Add(propertyCount);
    s_payloadBytes.Add(payloadBytes);
    s_elapsedMs.Add(elapsedMs);
  }

  public static PropertyExtractionMetricsSnapshot Snapshot()
  {
    var totalCategoryCounts = s_totalCategoryCounts.ToArray();
    var userFilteredCategoryCounts = s_userFilteredCategoryCounts.ToArray();
    var propertyCounts = s_propertyCounts.ToArray();
    var payloadBytes = s_payloadBytes.ToArray();
    var elapsedMs = s_elapsedMs.ToArray();

    return new PropertyExtractionMetricsSnapshot(
      ObjectCount: propertyCounts.Length,
      AvgTotalCategoryCount: Mean(totalCategoryCounts),
      AvgUserFilteredCategoryCount: Mean(userFilteredCategoryCounts),
      AvgPropertyCount: Mean(propertyCounts),
      P95PropertyCount: P95(propertyCounts),
      AvgPayloadBytes: Mean(payloadBytes),
      P95PayloadBytes: P95(payloadBytes),
      TotalPayloadBytes: payloadBytes.Sum(v => (long)v),
      AvgExtractionMs: Mean(elapsedMs),
      P95ExtractionMs: P95(elapsedMs)
    );
  }

  private static double Mean(int[] values) => values.Length == 0 ? 0 : values.Average();

  private static double Mean(double[] values) => values.Length == 0 ? 0 : values.Average();

  private static double P95(int[] values)
  {
    if (values.Length == 0)
    {
      return 0;
    }

    Array.Sort(values);
    int index = (int)Math.Ceiling(values.Length * 0.95) - 1;
    int clampedIndex = Math.Max(0, Math.Min(index, values.Length - 1));
    return values[clampedIndex];
  }

  private static double P95(double[] values)
  {
    if (values.Length == 0)
    {
      return 0;
    }

    Array.Sort(values);
    int index = (int)Math.Ceiling(values.Length * 0.95) - 1;
    int clampedIndex = Math.Max(0, Math.Min(index, values.Length - 1));
    return values[clampedIndex];
  }

  private static void ClearBag<T>(ConcurrentBag<T> bag)
  {
    while (bag.TryTake(out _)) { }
  }
}

public readonly record struct PropertyExtractionMetricsSnapshot(
  int ObjectCount,
  double AvgTotalCategoryCount,
  double AvgUserFilteredCategoryCount,
  double AvgPropertyCount,
  double P95PropertyCount,
  double AvgPayloadBytes,
  double P95PayloadBytes,
  long TotalPayloadBytes,
  double AvgExtractionMs,
  double P95ExtractionMs
);
