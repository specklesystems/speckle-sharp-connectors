using System.Collections.Concurrent;

namespace Speckle.Converter.Navisworks.Services;

public static class GeometryConversionMetricsTracker
{
  private static long s_toInwOpSelectionCalls;
  private static long s_convertedObjectCount;
  private static long s_pathsProcessed;
  private static long s_fragmentsProcessed;
  private static readonly ConcurrentBag<double> s_selectionElapsedMs = [];

  public static void Reset()
  {
    Interlocked.Exchange(ref s_toInwOpSelectionCalls, 0);
    Interlocked.Exchange(ref s_convertedObjectCount, 0);
    Interlocked.Exchange(ref s_pathsProcessed, 0);
    Interlocked.Exchange(ref s_fragmentsProcessed, 0);
    ClearBag(s_selectionElapsedMs);
  }

  public static void Record(int convertedObjectCount, int pathCount, int fragmentCount, double elapsedMs)
  {
    Interlocked.Increment(ref s_toInwOpSelectionCalls);
    Interlocked.Add(ref s_convertedObjectCount, convertedObjectCount);
    Interlocked.Add(ref s_pathsProcessed, pathCount);
    Interlocked.Add(ref s_fragmentsProcessed, fragmentCount);
    s_selectionElapsedMs.Add(elapsedMs);
  }

  public static GeometryConversionMetricsSnapshot Snapshot()
  {
    var selectionElapsedMs = s_selectionElapsedMs.ToArray();
    long selectionCalls = Interlocked.Read(ref s_toInwOpSelectionCalls);
    long convertedObjectCount = Interlocked.Read(ref s_convertedObjectCount);
    long pathsProcessed = Interlocked.Read(ref s_pathsProcessed);
    long fragmentsProcessed = Interlocked.Read(ref s_fragmentsProcessed);
    double totalSelectionElapsedMs = selectionElapsedMs.Sum();

    return new GeometryConversionMetricsSnapshot(
      ToInwOpSelectionCalls: selectionCalls,
      ConvertedObjectCount: convertedObjectCount,
      PathsProcessed: pathsProcessed,
      FragmentsProcessed: fragmentsProcessed,
      TotalSelectionElapsedMs: totalSelectionElapsedMs,
      AvgSelectionElapsedMs: Mean(selectionElapsedMs),
      P95SelectionElapsedMs: P95(selectionElapsedMs),
      AvgSelectionElapsedMsPerObject: convertedObjectCount == 0 ? 0 : totalSelectionElapsedMs / convertedObjectCount,
      AvgPathsPerSelection: selectionCalls == 0 ? 0 : (double)pathsProcessed / selectionCalls,
      AvgFragmentsPerPath: pathsProcessed == 0 ? 0 : (double)fragmentsProcessed / pathsProcessed
    );
  }

  private static double Mean(double[] values) => values.Length == 0 ? 0 : values.Average();

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

public readonly record struct GeometryConversionMetricsSnapshot(
  long ToInwOpSelectionCalls,
  long ConvertedObjectCount,
  long PathsProcessed,
  long FragmentsProcessed,
  double TotalSelectionElapsedMs,
  double AvgSelectionElapsedMs,
  double P95SelectionElapsedMs,
  double AvgSelectionElapsedMsPerObject,
  double AvgPathsPerSelection,
  double AvgFragmentsPerPath
);
