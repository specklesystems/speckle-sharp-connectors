using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Constants.Registers;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Paths;
using Speckle.Converter.Navisworks.Settings;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

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

    var comSelection = ComApiBridge.ToInwOpSelection(new() { modelItem });
    try
    {
      var paths = comSelection.Paths();
      try
      {
        var processors = new List<PrimitiveProcessor>();

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
          }

          var processor = new PrimitiveProcessor(_isUpright);
          ProcessPathFragments(path, itemPathKey, processor); // only current instance
          processors.Add(processor);

          _registry.MarkConverted(itemPathKey); // optional for later
        }

        return ProcessGeometries(processors);
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

      set.Add(PathKey.FromComArray(fragPathArr));
    }

    return set;
  }

  private static void ProcessPathFragments(InwOaPath path, PathKey itemPathKey, PrimitiveProcessor processor)
  {
    bool captured = false;
#pragma warning disable IDE0059
    double[]? instanceWorld = null;
#pragma warning restore IDE0059

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

      var matrix = fragment.GetLocalToWorldMatrix();
      if (matrix is not InwLTransform3f3 transform)
      {
        continue;
      }

      if (transform.Matrix is not Array matrixArray)
      {
        continue;
      }

      if (!captured)
      {
        // ReSharper disable once RedundantAssignment
        instanceWorld = ConvertArrayToDouble(matrixArray);
        captured = true;
        // breakpoint here
      }
      processor.LocalToWorldTransformation = ConvertArrayToDouble(matrixArray);
      fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
    }
  }

  private List<Base> ProcessGeometries(List<PrimitiveProcessor> processors)
  {
    var baseGeometries = new List<Base>();

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
    var vertices = new List<double>();
    var faces = new List<int>();

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

  private List<Line> CreateLines(IReadOnlyList<SafeLine> lines) =>
    (
      from line in lines
      select new Line
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
    ).ToList();

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
}
