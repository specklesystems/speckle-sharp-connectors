using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Constants;
using Speckle.Converter.Navisworks.Constants.Registers;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Paths;
using Speckle.Converter.Navisworks.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using static Speckle.Converter.Navisworks.Constants.InstanceConstants;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;
// ReSharper disable HeuristicUnreachableCode
#pragma warning disable CS0162 // Unreachable code detected

namespace Speckle.Converter.Navisworks.ToSpeckle;

/// <summary>
/// WARNING: Uses COM interop - cannot use public ModelGeometry API.
/// Process: ModelItem → InwOaPath3 → InwOaFragmentList → InwOaFragment3 → primitives → Speckle geometry
///
/// COM overhead: ~13.7ms per item (99.5% of the time) - cannot be optimized from C#
/// All COM objects are properly released in try-finally blocks to prevent memory leaks.
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
  private const bool ENABLE_INSTANCING = true;
  private readonly Dictionary<PathKey, int> _groupMemberCounts = new(PathKey.Comparer);

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

    NAV.ModelItemCollection collection = new() { modelItem };
    var comSelection = ComApiBridge.ToInwOpSelection(modelItemCollection: collection);
    try
    {
      var paths = comSelection.Paths();
      if (paths == null)
      {
        return [];
      }
      try
      {
        var allResults = new List<Base>(5);

        foreach (InwOaPath path in paths)
        {
          if (path.ArrayData is not Array pathArr)
          {
            continue;
          }

          var itemPathKey = PathKey.FromComArray(pathArr);

          if (!_registry.TryGetGroup(itemPathKey, out var groupKey))
          {
            var members = DiscoverInstancePathsFromFragments(path);
            members.Add(itemPathKey);
            groupKey = itemPathKey;
            _registry.RegisterGroup(groupKey, members);
            _groupMemberCounts[groupKey] = members.Count;
          }

          var processor = new PrimitiveProcessor(_isUpright);
          ProcessPathFragments(path, itemPathKey, groupKey, processor);

          if (!_registry.TryGetInstanceWorld(itemPathKey, out var instanceWorld))
          {
            var geometries = ProcessGeometries([processor]);
            _registry.MarkConverted(itemPathKey);
            allResults.AddRange(geometries);
            continue;
          }

          if (_groupMemberCounts.TryGetValue(groupKey, out var memberCount) && memberCount == 1)
          {
            var geometries = ProcessGeometries([processor]);
            _registry.MarkConverted(itemPathKey);
            allResults.AddRange(geometries);
            continue;
          }

          if (ENABLE_INSTANCING && !_registry.HasDefinitionGeometry(groupKey))
          {
            var geometries = ProcessGeometries([processor]);

            var invDefWorld = GeometryHelpers.InvertRigid(instanceWorld);
            var definitionGeometry = UnbakeGeometry(geometries, invDefWorld);
            var groupKeyHash = groupKey.ToHashString();
            for (int i = 0; i < definitionGeometry.Count; i++)
            {
              definitionGeometry[i].applicationId = $"{GEOMETRY_ID_PREFIX}{groupKeyHash}_{i}";
            }

            _registry.StoreDefinitionGeometry(groupKey, definitionGeometry);
          }

          if (ENABLE_INSTANCING)
          {
            var instanceProxy = new InstanceProxy
            {
              definitionId = $"{InstanceConstants.DEFINITION_ID_PREFIX}{groupKey.ToHashString()}",
              transform = ConvertToMatrix4X4(instanceWorld),
              units = _settings.Derived.SpeckleUnits,
              applicationId = $"{InstanceConstants.INSTANCE_ID_PREFIX}{itemPathKey.ToHashString()}",
              maxDepth = 0
            };

            _registry.MarkConverted(itemPathKey);
            allResults.Add(instanceProxy);
          }
          else
          {
            var geometries = ProcessGeometries([processor]);
            _registry.MarkConverted(itemPathKey);
            allResults.AddRange(geometries);
          }
        }

        return allResults;
      }
      finally
      {
        Marshal.ReleaseComObject(paths);
      }
    }
    finally
    {
      if (comSelection != null)
      {
        Marshal.ReleaseComObject(comSelection);
      }
    }
    collection.Dispose();
  }

  private static HashSet<PathKey> DiscoverInstancePathsFromFragments(InwOaPath path)
  {
    var set = new HashSet<PathKey>(PathKey.Comparer);
    var fragments = path.Fragments();

    try
    {
      foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
      {
        GC.KeepAlive(fragment);

        InwOaPath? fragPath = fragment.path;
        if (fragPath?.ArrayData is not Array fragPathArr)
        {
          continue;
        }

        var fragmentPathKey = PathKey.FromComArray(fragPathArr);
        set.Add(fragmentPathKey);

        Marshal.ReleaseComObject(fragPath);
      }
    }
    finally
    {
      if (fragments != null)
      {
        Marshal.ReleaseComObject(fragments);
      }
    }

    return set;
  }

  private void ProcessPathFragments(InwOaPath path, PathKey itemPathKey, PathKey groupKey, PrimitiveProcessor processor)
  {
    var observed = false;
    var fragments = path.Fragments();

    try
    {
      foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
      {
        GC.KeepAlive(fragment);

        InwOaPath? fragPath = null;
        InwLTransform3f3? transform = null;

        try
        {
          fragPath = fragment.path;
          if (fragPath?.ArrayData is not Array fragPathArr)
          {
            continue;
          }

          if (!itemPathKey.MatchesComArray(fragPathArr))
          {
            continue;
          }

          transform = fragment.GetLocalToWorldMatrix() as InwLTransform3f3;
          if (transform == null)
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

          if (processor.Triangles.Count <= 0 && processor.Lines.Count <= 0)
          {
            continue;
          }

          _registry.RegisterInstanceObservation(groupKey, itemPathKey, instanceWorld, processor);
          observed = true;
        }
        finally
        {
          if (transform != null)
          {
            Marshal.ReleaseComObject(transform);
          }
          if (fragPath != null)
          {
            Marshal.ReleaseComObject(fragPath);
          }
        }
      }
    }
    finally
    {
      if (fragments != null)
      {
        Marshal.ReleaseComObject(fragments);
      }
    }
  }

  private List<Base> ProcessGeometries(List<PrimitiveProcessor> processors)
  {
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

  private Mesh CreateMesh(IReadOnlyList<SafeTriangle> triangles)
  {
    var vertices = new List<double>(triangles.Count * 9);
    var faces = new List<int>(triangles.Count * 4);

    for (var t = 0; t < triangles.Count; t++)
    {
      var triangle = triangles[t];
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
  /// VALIDATION HELPER: Unbakes geometry from world space to definition space. Creates copies of the geometry and
  /// applies inverse transform to move from world coordinates back to definition/local space.
  /// Used for visual validation of instance detection.
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

}
