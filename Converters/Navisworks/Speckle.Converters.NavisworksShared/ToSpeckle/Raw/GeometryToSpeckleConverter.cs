using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Extensions;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
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
public class GeometryToSpeckleConverter
{
  private readonly NavisworksConversionSettings _settings;
  private readonly bool _isUpright;
  private readonly SafeVector _transformVector;
  private const double SCALE = 1.0; // Default scale factor

  public GeometryToSpeckleConverter(NavisworksConversionSettings settings)
  {
    _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    _isUpright = settings.Derived.IsUpright;
    _transformVector = settings.Derived.TransformVector;
  }

  /// <summary>
  /// Converts a ModelItem's geometry to Speckle display geometry by accessing the underlying COM objects.
  /// Applies necessary transformations and unit scaling.
  /// </summary>
  internal List<Base> Convert(NAV.ModelItem modelItem, bool isInstanceDefinition = false)
  {
    if (modelItem == null)
    {
      throw new ArgumentNullException(nameof(modelItem));
    }

    if (!modelItem.HasGeometry)
    {
      return [];
    }

    var comSelection = ComApiBridge.ToInwOpSelection([modelItem]);
    try
    {
      var fragmentStack = new Stack<InwOaFragment3>();
      var paths = comSelection.Paths();
      try
      {
        // Populate fragment stack with all fragments
        foreach (InwOaPath path in paths)
        {
          CollectFragments(path, fragmentStack);
        }

        return ProcessFragments(fragmentStack, paths, isInstanceDefinition);
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

  private static void CollectFragments(InwOaPath path, Stack<InwOaFragment3> fragmentStack)
  {
    var fragments = path.Fragments();
    foreach (var fragment in fragments.OfType<InwOaFragment3>())
    {
      if (fragment.path?.ArrayData is not Array pathData1 || path.ArrayData is not Array pathData2)
      {
        continue;
      }

      var pathArray1 = pathData1.ToArray<int>();
      var pathArray2 = pathData2.ToArray<int>();

      if (pathArray1.Length == pathArray2.Length && pathArray1.SequenceEqual(pathArray2))
      {
        fragmentStack.Push(fragment);
      }
    }
  }

  private List<Base> ProcessFragments(Stack<InwOaFragment3> fragmentStack, InwSelectionPathsColl paths, bool isInstanceDefinition = false)
  {
    var callbackListeners = new List<PrimitiveProcessor>();

    foreach (InwOaPath path in paths)
    {
      var processor = new PrimitiveProcessor(_isUpright);

      foreach (var fragment in fragmentStack)
      {
        if (!ValidateFragmentPath(fragment, path))
        {
          continue;
        }

        var matrix = fragment.GetLocalToWorldMatrix();
        var transform = matrix as InwLTransform3f3;
        if (transform?.Matrix is not Array matrixArray)
        {
          continue;
        }

        processor.LocalToWorldTransformation = ConvertArrayToDouble(matrixArray);
        fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
      }

      callbackListeners.Add(processor);
    }

    var baseGeometries = ProcessGeometries(callbackListeners, isInstanceDefinition);

    return baseGeometries;
  }

  private static bool ValidateFragmentPath(InwOaFragment3 fragment, InwOaPath path)
  {
    if (fragment.path?.ArrayData is not Array fragmentPathData || path.ArrayData is not Array pathData)
    {
      return false;
    }

    return IsSameFragmentPath(fragmentPathData, pathData);
  }

  private List<Base> ProcessGeometries(List<PrimitiveProcessor> processors, bool isInstanceDefinition = false)
  {
    var baseGeometries = new List<Base>();

    foreach (var processor in processors)
    {
      if (processor.Triangles.Count > 0)
      {
        var mesh = CreateMesh(processor.Triangles, isInstanceDefinition);
        baseGeometries.Add(mesh);
      }

      if (processor.Lines.Count > 0)
      {
        var lines = CreateLines(processor.Lines, isInstanceDefinition);
        baseGeometries.AddRange(lines);
      }
    }

    return baseGeometries;
  }

  private Mesh CreateMesh(IReadOnlyList<SafeTriangle> triangles, bool isInstanceDefinition = false)
  {
    var vertices = new List<double>();
    var faces = new List<int>();

    for (var t = 0; t < triangles.Count; t++)
    {
      var triangle = triangles[t];

      // For instance definitions, don't apply global transform - only apply coordinate system and scaling
      if (isInstanceDefinition)
      {
        vertices.AddRange(
          [
            triangle.Vertex1.X * SCALE,
            triangle.Vertex1.Y * SCALE,
            triangle.Vertex1.Z * SCALE,
            triangle.Vertex2.X * SCALE,
            triangle.Vertex2.Y * SCALE,
            triangle.Vertex2.Z * SCALE,
            triangle.Vertex3.X * SCALE,
            triangle.Vertex3.Y * SCALE,
            triangle.Vertex3.Z * SCALE
          ]
        );
      }
      else
      {
        // For non-instance geometry, apply global transform as before
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
      }
      faces.AddRange([3, t * 3, t * 3 + 1, t * 3 + 2]);
    }

    return new Mesh
    {
      vertices = vertices,
      faces = faces,
      units = _settings.Derived.SpeckleUnits
    };
  }

  private List<Line> CreateLines(IReadOnlyList<SafeLine> lines, bool isInstanceDefinition = false) =>
    (
      from line in lines
      select new Line
      {
        start = isInstanceDefinition
          ? new Point(
              line.Start.X * SCALE,
              line.Start.Y * SCALE,
              line.Start.Z * SCALE,
              _settings.Derived.SpeckleUnits
            )
          : new Point(
              (line.Start.X + _transformVector.X) * SCALE,
              (line.Start.Y + _transformVector.Y) * SCALE,
              (line.Start.Z + _transformVector.Z) * SCALE,
              _settings.Derived.SpeckleUnits
            ),
        end = isInstanceDefinition
          ? new Point(
              line.End.X * SCALE,
              line.End.Y * SCALE,
              line.End.Z * SCALE,
              _settings.Derived.SpeckleUnits
            )
          : new Point(
              (line.End.X + _transformVector.X) * SCALE,
              (line.End.Y + _transformVector.Y) * SCALE,
              (line.End.Z + _transformVector.Z) * SCALE,
              _settings.Derived.SpeckleUnits
            ),
        units = _settings.Derived.SpeckleUnits
      }
    ).ToList();

  private static double[]? ConvertArrayToDouble(Array arr)
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

  private static bool IsSameFragmentPath(Array a1, Array a2) =>
    a1.Length == a2.Length && a1.Cast<int>().SequenceEqual(a2.Cast<int>());
}
