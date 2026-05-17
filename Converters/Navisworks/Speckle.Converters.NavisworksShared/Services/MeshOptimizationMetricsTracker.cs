using System.Threading;

namespace Speckle.Converter.Navisworks.Services;

public static class MeshOptimizationMetricsTracker
{
  private static long s_meshObjectCount;
  private static long s_emptyGeometryObjectCount;
  private static long s_faceCount;
  private static long s_lineCount;
  private static long s_vertexCountBeforeWeld;
  private static long s_vertexCountAfterWeld;
  private static long s_totalWeldMsTicks;

  public static void Reset()
  {
    Interlocked.Exchange(ref s_meshObjectCount, 0);
    Interlocked.Exchange(ref s_emptyGeometryObjectCount, 0);
    Interlocked.Exchange(ref s_faceCount, 0);
    Interlocked.Exchange(ref s_lineCount, 0);
    Interlocked.Exchange(ref s_vertexCountBeforeWeld, 0);
    Interlocked.Exchange(ref s_vertexCountAfterWeld, 0);
    Interlocked.Exchange(ref s_totalWeldMsTicks, 0);
  }

  public static void RecordMesh(
    int faceCount,
    int vertexCountBeforeWeld,
    int vertexCountAfterWeld,
    double weldMs,
    bool isEmpty
  )
  {
    Interlocked.Increment(ref s_meshObjectCount);
    Interlocked.Add(ref s_faceCount, faceCount);
    Interlocked.Add(ref s_vertexCountBeforeWeld, vertexCountBeforeWeld);
    Interlocked.Add(ref s_vertexCountAfterWeld, vertexCountAfterWeld);

    long ticks = (long)Math.Round(weldMs * TimeSpan.TicksPerMillisecond);
    Interlocked.Add(ref s_totalWeldMsTicks, ticks);

    if (isEmpty)
    {
      Interlocked.Increment(ref s_emptyGeometryObjectCount);
    }
  }

  public static void RecordLines(int lineCount) => Interlocked.Add(ref s_lineCount, lineCount);

  public static MeshOptimizationMetricsSnapshot Snapshot()
  {
    long before = Interlocked.Read(ref s_vertexCountBeforeWeld);
    long after = Interlocked.Read(ref s_vertexCountAfterWeld);
    long meshObjectCount = Interlocked.Read(ref s_meshObjectCount);

    double reductionPercent = before == 0 ? 0 : ((double)(before - after) / before) * 100.0;
    double totalWeldMs = Interlocked.Read(ref s_totalWeldMsTicks) / (double)TimeSpan.TicksPerMillisecond;

    return new MeshOptimizationMetricsSnapshot(
      MeshObjectCount: meshObjectCount,
      EmptyGeometryObjectCount: Interlocked.Read(ref s_emptyGeometryObjectCount),
      FaceCount: Interlocked.Read(ref s_faceCount),
      LineCount: Interlocked.Read(ref s_lineCount),
      VertexCountBeforeWeld: before,
      VertexCountAfterWeld: after,
      VertexReductionPercent: reductionPercent,
      MeshWeldMs: totalWeldMs,
      AvgVerticesPerObject: meshObjectCount == 0 ? 0 : (double)after / meshObjectCount
    );
  }
}

public readonly record struct MeshOptimizationMetricsSnapshot(
  long MeshObjectCount,
  long EmptyGeometryObjectCount,
  long FaceCount,
  long LineCount,
  long VertexCountBeforeWeld,
  long VertexCountAfterWeld,
  double VertexReductionPercent,
  double MeshWeldMs,
  double AvgVerticesPerObject
);
