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
  private static long s_seamRetentionEnabled;
  private static long s_geometryDetailLevel;
  private static long s_creaseAngleDegreesBits;

  public static void Reset()
  {
    Interlocked.Exchange(ref s_meshObjectCount, 0);
    Interlocked.Exchange(ref s_emptyGeometryObjectCount, 0);
    Interlocked.Exchange(ref s_faceCount, 0);
    Interlocked.Exchange(ref s_lineCount, 0);
    Interlocked.Exchange(ref s_vertexCountBeforeWeld, 0);
    Interlocked.Exchange(ref s_vertexCountAfterWeld, 0);
    Interlocked.Exchange(ref s_totalWeldMsTicks, 0);
    Interlocked.Exchange(ref s_seamRetentionEnabled, 0);
    Interlocked.Exchange(ref s_geometryDetailLevel, (long)Settings.GeometryDetailLevel.Full);
    Interlocked.Exchange(ref s_creaseAngleDegreesBits, BitConverter.DoubleToInt64Bits(0));
  }

  public static void RecordSettings(
    bool seamRetentionEnabled,
    Settings.GeometryDetailLevel geometryDetailLevel,
    double creaseAngleDegrees
  )
  {
    Interlocked.Exchange(ref s_seamRetentionEnabled, seamRetentionEnabled ? 1 : 0);
    Interlocked.Exchange(ref s_geometryDetailLevel, (long)geometryDetailLevel);
    Interlocked.Exchange(ref s_creaseAngleDegreesBits, BitConverter.DoubleToInt64Bits(creaseAngleDegrees));
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
      AvgVerticesPerObject: meshObjectCount == 0 ? 0 : (double)after / meshObjectCount,
      SeamRetentionEnabled: Interlocked.Read(ref s_seamRetentionEnabled) == 1,
      GeometryDetailLevel: (Settings.GeometryDetailLevel)Interlocked.Read(ref s_geometryDetailLevel),
      CreaseAngleDegrees: BitConverter.Int64BitsToDouble(Interlocked.Read(ref s_creaseAngleDegreesBits))
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
  double AvgVerticesPerObject,
  bool SeamRetentionEnabled,
  Settings.GeometryDetailLevel GeometryDetailLevel,
  double CreaseAngleDegrees
);
