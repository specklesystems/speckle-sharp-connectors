using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Converters.Common;

namespace Speckle.Connector.Navisworks.Operations.Send;

public readonly record struct NavisworksGcSnapshot(
  int Gen0Start,
  int Gen1Start,
  int Gen2Start,
  long ManagedHeapBytesStart
)
{
  public static NavisworksGcSnapshot Capture() =>
    new(GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2), GC.GetTotalMemory(false));
}

public sealed class NavisworksSendBenchmarkLogger(
  ILogger<NavisworksSendBenchmarkLogger> logger,
  IConverterSettingsStore<NavisworksConversionSettings> converterSettings,
  IModelItemPropertySetsCache modelItemPropertySetsCache
)
{
  public void LogConversionMetrics()
  {
    LogPropertyExtractionMetrics();
    LogGeometryConversionMetrics();
    LogMeshOptimizationMetrics();
  }

  public void LogBuildBenchmarkSummary(
    long conversionStartMs,
    long conversionEndMs,
    double reassemblyMs,
    int finalElementCount,
    NavisworksGcSnapshot gcSnapshot
  )
  {
    var user = converterSettings.Current.User;
    var propertySnapshot = PropertyExtractionMetricsTracker.Snapshot();
    var meshSnapshot = MeshOptimizationMetricsTracker.Snapshot();
    var conversionMs = conversionEndMs - conversionStartMs;
    var (gcGen0Delta, gcGen1Delta, gcGen2Delta, managedHeapBytesEnd) = MeasureGcDeltas(gcSnapshot);

    logger.LogInformation(
      "Build benchmark summary: geometryPreset={GeometryPreset}, propertyPreset={PropertyPreset}, roundMeshVertexDoubles={RoundMeshVertexDoubles}, includeInternalProperties={IncludeInternalProperties}, preserveHierarchy={PreserveHierarchy}, seamRetentionEnabled={SeamRetentionEnabled}, creaseAngleDegrees={CreaseAngleDegrees:F1}, conversionMs={ConversionMs}, reassemblyMs={ReassemblyMs:F2}, totalMeasuredMs={TotalMeasuredMs:F2}, finalElementCount={FinalElementCount}, propertyObjectCount={PropertyObjectCount}, avgPropertiesPerObject={AvgPropertiesPerObject:F2}, p95PropertiesPerObject={P95PropertiesPerObject:F0}, meshObjectCount={MeshObjectCount}, vertexCountBeforeWeld={VertexCountBeforeWeld}, vertexCountAfterWeld={VertexCountAfterWeld}, vertexReductionPercent={VertexReductionPercent:F2}, managedHeapBytesStart={ManagedHeapBytesStart}, managedHeapBytesEnd={ManagedHeapBytesEnd}, gcCollectionsGen0={GcCollectionsGen0}, gcCollectionsGen1={GcCollectionsGen1}, gcCollectionsGen2={GcCollectionsGen2}",
      user.GeometryDetailLevel,
      user.PropertyDetailLevel,
      user.RoundMeshVertexDoubles,
      user.IncludeInternalProperties,
      user.PreserveModelHierarchy,
      meshSnapshot.SeamRetentionEnabled,
      meshSnapshot.CreaseAngleDegrees,
      conversionMs,
      reassemblyMs,
      conversionMs + reassemblyMs,
      finalElementCount,
      propertySnapshot.ObjectCount,
      propertySnapshot.AvgPropertyCount,
      propertySnapshot.P95PropertyCount,
      meshSnapshot.MeshObjectCount,
      meshSnapshot.VertexCountBeforeWeld,
      meshSnapshot.VertexCountAfterWeld,
      meshSnapshot.VertexReductionPercent,
      gcSnapshot.ManagedHeapBytesStart,
      managedHeapBytesEnd,
      gcGen0Delta,
      gcGen1Delta,
      gcGen2Delta
    );
  }

  public void LogSendPhaseTimingMetrics(
    long conversionStartMs,
    long conversionEndMs,
    double serializationMs,
    double uploadMs,
    int serializedObjectCount,
    NavisworksGcSnapshot gcSnapshot
  )
  {
    var geometrySnapshot = GeometryConversionMetricsTracker.Snapshot();
    var propertySnapshot = PropertyExtractionMetricsTracker.Snapshot();
    long peakWorkingSetBytes = Process.GetCurrentProcess().PeakWorkingSet64;
    double propertyExtractionMs = propertySnapshot.AvgExtractionMs * propertySnapshot.ObjectCount;
    var (gcGen0Delta, gcGen1Delta, gcGen2Delta, managedHeapBytesEnd) = MeasureGcDeltas(gcSnapshot);

    logger.LogInformation(
      "Send phase metrics: conversionStartMs={ConversionStartMs}, conversionEndMs={ConversionEndMs}, conversionMs={ConversionMs}, geometryExtractionMs={GeometryExtractionMs:F2}, propertyExtractionMs={PropertyExtractionMs:F2}, serializationMs={SerializationMs:F2}, uploadMs={UploadMs:F2}, cleanupMs={CleanupMs:F2}, serializedObjectCount={SerializedObjectCount}, serializedBytesUncompressed={SerializedBytesUncompressed}, serializedBytesCompressed={SerializedBytesCompressed}, peakWorkingSetBytes={PeakWorkingSetBytes}, managedHeapBytesStart={ManagedHeapBytesStart}, managedHeapBytesEnd={ManagedHeapBytesEnd}, gcCollectionsGen0={GcCollectionsGen0}, gcCollectionsGen1={GcCollectionsGen1}, gcCollectionsGen2={GcCollectionsGen2}",
      conversionStartMs,
      conversionEndMs,
      conversionEndMs - conversionStartMs,
      geometrySnapshot.TotalSelectionElapsedMs,
      propertyExtractionMs,
      serializationMs,
      uploadMs,
      0d,
      serializedObjectCount,
      -1L,
      -1L,
      peakWorkingSetBytes,
      gcSnapshot.ManagedHeapBytesStart,
      managedHeapBytesEnd,
      gcGen0Delta,
      gcGen1Delta,
      gcGen2Delta
    );
  }

  public void LogSendBenchmarkSummary(
    long conversionStartMs,
    long conversionEndMs,
    double serializationMs,
    double uploadMs,
    int finalElementCount,
    int serializedObjectCount
  )
  {
    var user = converterSettings.Current.User;
    var propertySnapshot = PropertyExtractionMetricsTracker.Snapshot();
    var meshSnapshot = MeshOptimizationMetricsTracker.Snapshot();
    var postConversionMs = serializationMs + uploadMs;
    var conversionMs = conversionEndMs - conversionStartMs;

    logger.LogInformation(
      "Benchmark summary: geometryPreset={GeometryPreset}, propertyPreset={PropertyPreset}, roundMeshVertexDoubles={RoundMeshVertexDoubles}, includeInternalProperties={IncludeInternalProperties}, preserveHierarchy={PreserveHierarchy}, seamRetentionEnabled={SeamRetentionEnabled}, creaseAngleDegrees={CreaseAngleDegrees:F1}, conversionMs={ConversionMs}, postConversionMs={PostConversionMs:F2}, serializationMs={SerializationMs:F2}, uploadMs={UploadMs:F2}, totalMeasuredMs={TotalMeasuredMs:F2}, finalElementCount={FinalElementCount}, serializedObjectCount={SerializedObjectCount}, propertyObjectCount={PropertyObjectCount}, avgPropertiesPerObject={AvgPropertiesPerObject:F2}, p95PropertiesPerObject={P95PropertiesPerObject:F0}, meshObjectCount={MeshObjectCount}, vertexCountBeforeWeld={VertexCountBeforeWeld}, vertexCountAfterWeld={VertexCountAfterWeld}, vertexReductionPercent={VertexReductionPercent:F2}",
      user.GeometryDetailLevel,
      user.PropertyDetailLevel,
      user.RoundMeshVertexDoubles,
      user.IncludeInternalProperties,
      user.PreserveModelHierarchy,
      meshSnapshot.SeamRetentionEnabled,
      meshSnapshot.CreaseAngleDegrees,
      conversionMs,
      postConversionMs,
      serializationMs,
      uploadMs,
      conversionMs + postConversionMs,
      finalElementCount,
      serializedObjectCount,
      propertySnapshot.ObjectCount,
      propertySnapshot.AvgPropertyCount,
      propertySnapshot.P95PropertyCount,
      meshSnapshot.MeshObjectCount,
      meshSnapshot.VertexCountBeforeWeld,
      meshSnapshot.VertexCountAfterWeld,
      meshSnapshot.VertexReductionPercent
    );
  }

  private void LogPropertyExtractionMetrics()
  {
    var snapshot = PropertyExtractionMetricsTracker.Snapshot();
    var cacheSnapshot = modelItemPropertySetsCache.Snapshot();
    logger.LogInformation(
      "Property extraction metrics: objects={ObjectCount}, avgTotalCategories={AvgTotalCategoryCount:F2}, avgUserFilteredCategories={AvgUserFilteredCategoryCount:F2}, avgProperties={AvgPropertyCount:F2}, p95Properties={P95PropertyCount:F0}, avgPayloadBytes={AvgPayloadBytes:F2}, p95PayloadBytes={P95PayloadBytes:F0}, totalPayloadBytes={TotalPayloadBytes}, avgExtractionMs={AvgExtractionMs:F2}, p95ExtractionMs={P95ExtractionMs:F2}, propertySetsCachedNodes={PropertySetsCachedNodes}, propertySetsCacheHits={PropertySetsCacheHits}, propertySetsCacheMisses={PropertySetsCacheMisses}",
      snapshot.ObjectCount,
      snapshot.AvgTotalCategoryCount,
      snapshot.AvgUserFilteredCategoryCount,
      snapshot.AvgPropertyCount,
      snapshot.P95PropertyCount,
      snapshot.AvgPayloadBytes,
      snapshot.P95PayloadBytes,
      snapshot.TotalPayloadBytes,
      snapshot.AvgExtractionMs,
      snapshot.P95ExtractionMs,
      cacheSnapshot.CachedNodeCount,
      cacheSnapshot.CacheHits,
      cacheSnapshot.CacheMisses
    );
  }

  private void LogGeometryConversionMetrics()
  {
    var snapshot = GeometryConversionMetricsTracker.Snapshot();
    logger.LogInformation(
      "Geometry conversion metrics: toInwOpSelectionCalls={ToInwOpSelectionCalls}, convertedObjects={ConvertedObjectCount}, pathsProcessed={PathsProcessed}, fragmentsProcessed={FragmentsProcessed}, totalSelectionMs={TotalSelectionElapsedMs:F2}, avgSelectionMs={AvgSelectionElapsedMs:F2}, p95SelectionMs={P95SelectionElapsedMs:F2}, avgSelectionMsPerObject={AvgSelectionElapsedMsPerObject:F4}, avgPathsPerSelection={AvgPathsPerSelection:F2}, avgFragmentsPerPath={AvgFragmentsPerPath:F2}",
      snapshot.ToInwOpSelectionCalls,
      snapshot.ConvertedObjectCount,
      snapshot.PathsProcessed,
      snapshot.FragmentsProcessed,
      snapshot.TotalSelectionElapsedMs,
      snapshot.AvgSelectionElapsedMs,
      snapshot.P95SelectionElapsedMs,
      snapshot.AvgSelectionElapsedMsPerObject,
      snapshot.AvgPathsPerSelection,
      snapshot.AvgFragmentsPerPath
    );
  }

  private void LogMeshOptimizationMetrics()
  {
    var snapshot = MeshOptimizationMetricsTracker.Snapshot();
    logger.LogInformation(
      "Mesh optimization metrics: meshObjectCount={MeshObjectCount}, emptyGeometryObjectCount={EmptyGeometryObjectCount}, faceCount={FaceCount}, lineCount={LineCount}, vertexCountBeforeWeld={VertexCountBeforeWeld}, vertexCountAfterWeld={VertexCountAfterWeld}, vertexReductionPercent={VertexReductionPercent:F2}, meshWeldMs={MeshWeldMs:F2}, avgVerticesPerObject={AvgVerticesPerObject:F2}, geometryDetailLevel={GeometryDetailLevel}, seamRetentionEnabled={SeamRetentionEnabled}, creaseAngleDegrees={CreaseAngleDegrees:F1}",
      snapshot.MeshObjectCount,
      snapshot.EmptyGeometryObjectCount,
      snapshot.FaceCount,
      snapshot.LineCount,
      snapshot.VertexCountBeforeWeld,
      snapshot.VertexCountAfterWeld,
      snapshot.VertexReductionPercent,
      snapshot.MeshWeldMs,
      snapshot.AvgVerticesPerObject,
      snapshot.GeometryDetailLevel,
      snapshot.SeamRetentionEnabled,
      snapshot.CreaseAngleDegrees
    );
  }

  private static (int Gen0Delta, int Gen1Delta, int Gen2Delta, long ManagedHeapBytesEnd) MeasureGcDeltas(
    NavisworksGcSnapshot gcSnapshot
  ) =>
    (
      GC.CollectionCount(0) - gcSnapshot.Gen0Start,
      GC.CollectionCount(1) - gcSnapshot.Gen1Start,
      GC.CollectionCount(2) - gcSnapshot.Gen2Start,
      GC.GetTotalMemory(false)
    );
}
