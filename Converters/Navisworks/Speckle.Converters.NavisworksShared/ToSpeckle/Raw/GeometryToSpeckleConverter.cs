using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Constants.Registers;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Paths;
using Speckle.Converter.Navisworks.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;
// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

namespace Speckle.Converter.Navisworks.ToSpeckle;

/// <summary>
/// Converts Navisworks geometry to Speckle displayable geometry.
///
/// Note: This class does not implement ITypedConverter{ModelGeometry, Base} because Navisworks geometry
/// conversion requires COM interop access that isn't available through the public ModelGeometry class.
/// The conversion process requires:
/// 1. Convert ModelItem to InwOaPath3 via ComApiBridge
/// 2. Use that to get InwOaFragmentList
/// 3. Process each InwOaFragment3 to generate primitives
/// 4. Convert those primitives to Speckle geometry with appropriate transforms
/// </summary>
public sealed class GeometryToSpeckleConverter(
  NavisworksConversionSettings settings,
  IInstanceFragmentRegistry registry
)
{
  private readonly NavisworksConversionSettings _settings =
    settings ?? throw new ArgumentNullException(nameof(settings));
  private readonly IInstanceFragmentRegistry _registry = registry ?? throw new ArgumentNullException(nameof(registry));
  private readonly bool _isUpright = settings.Derived.IsUpright;
  private readonly SafeVector _transformVector = settings.Derived.TransformVector;
  private const double SCALE = 1.0;

  // DIAGNOSTICS: Performance timing
  private long _comExtractionTicks;
  private long _geometryCreationTicks;
  private int _totalModelItemsProcessed;

  // INSTANCING: Set to true to skip geometry conversion for later instances
  // First instance becomes the definition, later instances return InstanceReference objects
  private const bool ENABLE_INSTANCING = true;

  // DIAGNOSTICS: Track grouping behavior
  private readonly Dictionary<PathKey, int> _groupMemberCounts = new(PathKey.Comparer);
  // ReSharper disable once NotAccessedField.Local
  private int _totalPathsProcessed;
  private int _singleMemberGroups;
  private int _multiMemberGroups;

  /// <summary>
  /// Gets performance statistics for COM extraction vs geometry creation.
  /// Returns (comMs, geometryMs, itemCount)
  /// </summary>
#pragma warning disable CA1024
  public (double comMs, double geometryMs, int itemCount) GetPerformanceStatistics()
#pragma warning restore CA1024
  {
    double comMs = _comExtractionTicks / (double)TimeSpan.TicksPerMillisecond;
    double geometryMs = _geometryCreationTicks / (double)TimeSpan.TicksPerMillisecond;
    return (comMs, geometryMs, _totalModelItemsProcessed);
  }

  internal List<Base> Convert(NAV.ModelItem modelItem)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    if (!modelItem.HasGeometry)
    {
      return [];
    }

    _totalModelItemsProcessed++;

    var comSelection = ComApiBridge.ToInwOpSelection(new() { modelItem });
    try
    {
      var paths = comSelection.Paths();
      try
      {
        // Pre-allocate for typical case: estimate ~5 geometry pieces per item
        var allResults = new List<Base>(5);

        foreach (InwOaPath path in paths)
        {
          if (path.ArrayData is not Array pathArr)
          {
            continue;
          }

          var itemPathKey = PathKey.FromComArray(pathArr);

          // discovery: populate registry for this group if first time
          if (!_registry.TryGetGroup(itemPathKey, out var groupKey))
          {
            var members = DiscoverInstancePathsFromFragments(path);
            members.Add(itemPathKey); // defensive
            groupKey = itemPathKey; // first seen
            _registry.RegisterGroup(groupKey, members);

            // DIAGNOSTICS: Track grouping statistics
            _totalPathsProcessed++;

            if (members.Count > 1)
            {
              _multiMemberGroups++;
            }
            else
            {
              _singleMemberGroups++;
            }

            _groupMemberCounts[groupKey] = members.Count;
          }

          // Extract instanceWorld from fragments FIRST (before any processing decisions)
          // This must happen before we can check if a definition exists or emit instance proxies
          var processor = new PrimitiveProcessor(_isUpright);

          // DIAGNOSTICS: Time COM extraction
          var comStopwatch = Stopwatch.StartNew();
          ProcessPathFragments(path, itemPathKey, groupKey, processor);
          comStopwatch.Stop();
          _comExtractionTicks += comStopwatch.ElapsedTicks;

          // Now instanceWorld should be stored via RegisterInstanceObservation
          if (!_registry.TryGetInstanceWorld(itemPathKey, out var instanceWorld))
          {
            // No valid instanceWorld found - process geometry normally without instancing
            // DIAGNOSTICS: Time geometry creation
            var geomStopwatch = Stopwatch.StartNew();
            var geometries = ProcessGeometries([processor]);
            geomStopwatch.Stop();
            _geometryCreationTicks += geomStopwatch.ElapsedTicks;

            _registry.MarkConverted(itemPathKey);
            allResults.AddRange(geometries);
            continue;
          }

          // OPTIMIZATION: Skip instancing for single-member groups
          // If this group only has 1 member, just return baked geometry directly
          if (_groupMemberCounts.TryGetValue(groupKey, out var memberCount) && memberCount == 1)
          {
            // Single member group - no benefit to instancing
            // Return the already-baked geometry (with transforms applied)
            // DIAGNOSTICS: Time geometry creation
            var geomStopwatch = Stopwatch.StartNew();
            var geometries = ProcessGeometries([processor]);
            geomStopwatch.Stop();
            _geometryCreationTicks += geomStopwatch.ElapsedTicks;

            _registry.MarkConverted(itemPathKey);
            allResults.AddRange(geometries);
            continue;
          }

          // INSTANCING: Check if this group needs its definition created
          if (ENABLE_INSTANCING && !_registry.HasDefinitionGeometry(groupKey))
          {
            // This is the first instance - convert and store as definition
            // DIAGNOSTICS: Time geometry creation
            var geomStopwatch = Stopwatch.StartNew();
            var geometries = ProcessGeometries([processor]);
            geomStopwatch.Stop();
            _geometryCreationTicks += geomStopwatch.ElapsedTicks;

            // Unbake geometry to definition space and store
            var invDefWorld = GeometryHelpers.InvertRigid(instanceWorld);
            var definitionGeometry = UnbakeGeometry(geometries, invDefWorld);

            // Set applicationId on definition geometry: "geom_{groupKeyHash}_{index}"
            var groupKeyHash = groupKey.ToHashString();
            for (int i = 0; i < definitionGeometry.Count; i++)
            {
              definitionGeometry[i].applicationId = $"geom_{groupKeyHash}_{i}";
            }

            _registry.StoreDefinitionGeometry(groupKey, definitionGeometry);
          }

          // ALL instances (including the first) emit InstanceProxy
          if (ENABLE_INSTANCING)
          {
            var instanceProxy = new InstanceProxy
            {
              definitionId = $"def_{groupKey.ToHashString()}",
              transform = ConvertToMatrix4X4(instanceWorld),
              units = _settings.Derived.SpeckleUnits,
              applicationId = $"instance_{itemPathKey.ToHashString()}",
              maxDepth = 0
            };

            _registry.MarkConverted(itemPathKey);
            allResults.Add(instanceProxy);
          }
          else
          {
            // Non-instancing mode: return the converted geometry
            // DIAGNOSTICS: Time geometry creation
            var geomStopwatch = Stopwatch.StartNew();
            var geometries = ProcessGeometries([processor]);
            geomStopwatch.Stop();
            _geometryCreationTicks += geomStopwatch.ElapsedTicks;

            _registry.MarkConverted(itemPathKey);
            allResults.AddRange(geometries);
          }
        }

        return allResults;
      }
      finally
      {
        if (paths != null)
        {
          Marshal.ReleaseComObject(paths);
        }
      }
    }
    finally
    {
      if (comSelection != null)
      {
        Marshal.ReleaseComObject(comSelection);
      }
    }
  }

  private static HashSet<PathKey> DiscoverInstancePathsFromFragments(InwOaPath path)
  {
    var set = new HashSet<PathKey>(PathKey.Comparer);

    foreach (InwOaFragment3 fragment in path.Fragments().OfType<InwOaFragment3>())
    {
      if (fragment.path?.ArrayData is not Array fragPathArr)
      {
        continue;
      }

      var fragmentPathKey = PathKey.FromComArray(fragPathArr);
      set.Add(fragmentPathKey);
    }

    return set;
  }

  private void ProcessPathFragments(InwOaPath path, PathKey itemPathKey, PathKey groupKey, PrimitiveProcessor processor)
  {
    var observed = false;

    foreach (InwOaFragment3 fragment in path.Fragments().OfType<InwOaFragment3>())
    {
      if (fragment.path?.ArrayData is not Array fragPathArr)
      {
        continue;
      }

      if (!itemPathKey.MatchesComArray(fragPathArr))
      {
        continue;
      }

      if (fragment.GetLocalToWorldMatrix() is not InwLTransform3f3 transform)
      {
        continue;
      }

      if (transform.Matrix is not Array matrixArray)
      {
        continue;
      }

      var instanceWorld = ConvertArrayToDouble(matrixArray);
      if (instanceWorld.Length != 16)
      {
        continue;
      }

      processor.LocalToWorldTransformation = instanceWorld;
      fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);

      if (observed)
      {
        continue;
      }

      // Observe only once, and only after we actually have some geometry
      if (processor.Triangles.Count <= 0 && processor.Lines.Count <= 0)
      {
        continue;
      }

      _registry.RegisterInstanceObservation(groupKey, itemPathKey, instanceWorld, processor);
      observed = true;
    }
  }

  private List<Base> ProcessGeometries(List<PrimitiveProcessor> processors)
  {
    // Pre-allocate: typically 1-2 geometries per processor (mesh + optional lines)
    var baseGeometries = new List<Base>(processors.Count * 2);

    foreach (var processor in processors)
    {
      if (processor.Triangles.Count > 0)
      {
        var mesh = CreateMesh(processor.Triangles);
        baseGeometries.Add(mesh);
      }

      if (processor.Lines.Count <= 0)
      {
        continue;
      }

      var lines = CreateLines(processor.Lines);
      baseGeometries.AddRange(lines);
    }

    return baseGeometries;
  }

  // ProcessGeometries, CreateMesh, CreateLines, ConvertArrayToDouble remain as-is
  private Mesh CreateMesh(IReadOnlyList<SafeTriangle> triangles)
  {
    // Pre-allocate: 9 doubles per triangle (3 vertices × 3 coords each)
    var vertices = new List<double>(triangles.Count * 9);
    // Pre-allocate: 4 ints per triangle (face count + 3 indices)
    var faces = new List<int>(triangles.Count * 4);

    for (var t = 0; t < triangles.Count; t++)
    {
      var triangle = triangles[t];

      // No need to worry about disposal of COM across boundaries - we're working with our safe structs
      vertices.AddRange(
        [
          (triangle.Vertex1.X + _transformVector.X) * SCALE,
          (triangle.Vertex1.Y + _transformVector.Y) * SCALE,
          (triangle.Vertex1.Z + _transformVector.Z) * SCALE,
          (triangle.Vertex2.X + _transformVector.X) * SCALE,
          (triangle.Vertex2.Y + _transformVector.Y) * SCALE,
          (triangle.Vertex2.Z + _transformVector.Z) * SCALE,
          (triangle.Vertex3.X + _transformVector.X) * SCALE,
          (triangle.Vertex3.Y + _transformVector.Y) * SCALE,
          (triangle.Vertex3.Z + _transformVector.Z) * SCALE
        ]
      );
      faces.AddRange([3, t * 3, t * 3 + 1, t * 3 + 2]);
    }

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = _settings.Derived.SpeckleUnits
    };
  }

  private List<Line> CreateLines(IReadOnlyList<SafeLine> lines)
  {
    // Pre-allocate with exact capacity to avoid resizing
    var result = new List<Line>(lines.Count);

    foreach (var line in lines)
    {
      result.Add(
        new Line
        {
          start = new Point(
            (line.Start.X + _transformVector.X) * SCALE,
            (line.Start.Y + _transformVector.Y) * SCALE,
            (line.Start.Z + _transformVector.Z) * SCALE,
            _settings.Derived.SpeckleUnits
          ),
          end = new Point(
            (line.End.X + _transformVector.X) * SCALE,
            (line.End.Y + _transformVector.Y) * SCALE,
            (line.End.Z + _transformVector.Z) * SCALE,
            _settings.Derived.SpeckleUnits
          ),
          units = _settings.Derived.SpeckleUnits
        }
      );
    }

    return result;
  }

  private static double[] ConvertArrayToDouble(Array arr)
  {
    if (arr.Rank != 1)
    {
      throw new ArgumentException("The input array must have a rank of 1.");
    }

    var doubleArray = new double[arr.GetLength(0)];
    for (var ix = arr.GetLowerBound(0); ix <= arr.GetUpperBound(0); ++ix)
    {
      doubleArray[ix - arr.GetLowerBound(0)] = (double)arr.GetValue(ix);
    }

    return doubleArray;
  }

  /// <summary>
  /// VALIDATION HELPER: Unbakes geometry from world space to definition space.
  /// Creates copies of the geometry and applies inverse transform to move from world coordinates
  /// back to definition/local space. Used for visual validation of instance detection.
  /// </summary>
  private static List<Base> UnbakeGeometry(List<Base> bakedGeometry, double[] invWorld)
  {
    var result = new List<Base>(bakedGeometry.Count);

    foreach (var item in bakedGeometry)
    {
      switch (item)
      {
        case Mesh mesh:
        {
          // Create a copy to avoid mutating the original
          var unbaked = new Mesh
          {
            vertices = [.. mesh.vertices],
            faces = mesh.faces,
            units = mesh.units
          };
          GeometryHelpers.UnbakeMeshVertices(unbaked, invWorld);
          result.Add(unbaked);
          break;
        }
        case Line line:
        {
          var unbaked = new Line
          {
            start = new Point(line.start.x, line.start.y, line.start.z, line.start.units),
            end = new Point(line.end.x, line.end.y, line.end.z, line.end.units),
            units = line.units
          };
          GeometryHelpers.UnbakeLine(unbaked, invWorld);
          result.Add(unbaked);
          break;
        }
        default:
          result.Add(item); // Pass through unknown types
          break;
      }
    }

    return result;
  }

  /// <summary>
  /// Converts a 16-element double array (row-major 4x4 matrix) to Matrix4x4 struct.
  /// </summary>
  private static Matrix4x4 ConvertToMatrix4X4(double[] matrix) =>
    matrix.Length == 16
      ? Matrix4x4.Transpose(
        new Matrix4x4
        {
          M11 = matrix[0],
          M12 = matrix[1],
          M13 = matrix[2],
          M14 = matrix[3],
          M21 = matrix[4],
          M22 = matrix[5],
          M23 = matrix[6],
          M24 = matrix[7],
          M31 = matrix[8],
          M32 = matrix[9],
          M33 = matrix[10],
          M34 = matrix[11],
          M41 = matrix[12],
          M42 = matrix[13],
          M43 = matrix[14],
          M44 = matrix[15]
        }
      )
      : throw new ArgumentException("Matrix must have exactly 16 elements", nameof(matrix));

  /// <summary>
  /// DIAGNOSTICS: Gets grouping statistics for analysis.
  /// Used to diagnose why grouping may fail with large selections.
  /// </summary>
  public (
    int totalGroups,
    int singleMember,
    int multiMember,
    int largestGroup,
    Dictionary<PathKey, int> groupCounts
  ) GetGroupingStatistics()
  {
    var largestGroup = _groupMemberCounts.Values.Count != 0 ? _groupMemberCounts.Values.Max() : 0;
    return (
      _groupMemberCounts.Count,
      _singleMemberGroups,
      _multiMemberGroups,
      largestGroup,
      new Dictionary<PathKey, int>(_groupMemberCounts, PathKey.Comparer)
    );
  }

  /// <summary>
  /// DIAGNOSTICS: Generates a summary report of grouping behavior.
  /// </summary>
  public string GetGroupingSummary()
  {
    if (_groupMemberCounts.Count == 0)
    {
      return "No grouping data collected yet.";
    }

    var sb = new StringBuilder();
    sb.AppendLine("=== Grouping Summary ===");
    sb.AppendLine($"Total Groups: {_groupMemberCounts.Count}");
    sb.AppendLine(
      $"Single-Member Groups: {_singleMemberGroups} ({GetPercentage(_singleMemberGroups, _groupMemberCounts.Count):F1}%)"
    );
    sb.AppendLine(
      $"Multi-Member Groups: {_multiMemberGroups} ({GetPercentage(_multiMemberGroups, _groupMemberCounts.Count):F1}%)"
    );
    sb.AppendLine(
      $"Largest Group: {(_groupMemberCounts.Values.Count != 0 ? _groupMemberCounts.Values.Max() : 0)} instances"
    );

    if (_multiMemberGroups <= 0)
    {
      return sb.ToString();
    }

    sb.AppendLine("\nTop 5 Groups:");
    var top5 = _groupMemberCounts.Where(kvp => kvp.Value > 1).OrderByDescending(kvp => kvp.Value).Take(5);

    int rank = 1;
    foreach (var kvp in top5)
    {
      sb.AppendLine($"  {rank}. Group {kvp.Key.ToHashString()}: {kvp.Value} instances");
      rank++;
    }

    return sb.ToString();
  }

  private static double GetPercentage(int part, int total) => total == 0 ? 0 : (double)part / total * 100.0;

  /// <summary>
  /// DIAGNOSTICS: Generates a performance timing report.
  /// </summary>
  public string GetPerformanceSummary()
  {
    var (comMs, geometryMs, itemCount) = GetPerformanceStatistics();
    var totalMs = comMs + geometryMs;

    if (itemCount == 0)
    {
      return "No performance data collected yet.";
    }

    var sb = new StringBuilder();
    sb.AppendLine("=== Performance Timing Summary ===");
    sb.AppendLine($"Total Items Processed: {itemCount}");
    sb.AppendLine($"Total Time: {totalMs:F2} ms");
    sb.AppendLine($"  COM Extraction: {comMs:F2} ms ({GetPercentage((int)comMs, (int)totalMs):F1}%)");
    sb.AppendLine($"  Geometry Creation: {geometryMs:F2} ms ({GetPercentage((int)geometryMs, (int)totalMs):F1}%)");
    sb.AppendLine($"Average per item:");
    sb.AppendLine($"  COM: {comMs / itemCount:F2} ms");
    sb.AppendLine($"  Geometry: {geometryMs / itemCount:F2} ms");

    return sb.ToString();
  }
}
