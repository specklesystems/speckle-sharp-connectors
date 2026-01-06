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
public class GeometryToSpeckleConverter(NavisworksConversionSettings settings)
{
  private readonly NavisworksConversionSettings _settings =
    settings ?? throw new ArgumentNullException(nameof(settings));
  private readonly bool _isUpright = settings.Derived.IsUpright;
  private readonly SafeVector _transformVector = settings.Derived.TransformVector;
  private const double SCALE = 1.0; // Default scale factor

  /// <summary>
  /// Converts a ModelItem's geometry to Speckle display geometry by accessing the underlying COM objects.
  /// Applies necessary transformations and unit scaling.
  /// </summary>
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

    var comSelection = ComApiBridge.ToInwOpSelection([modelItem]);
    try
    {
      var fragmentStack = new Stack<InwOaFragment3>();
      var paths = comSelection.Paths();
      try
      {
        // Populate the fragment stack with all fragments
        foreach (InwOaPath path in paths)
        {
          CollectFragments(path, fragmentStack);
        }

        return ProcessFragments(fragmentStack, paths);
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

  /// <summary>
  /// This collects fragments for the object path. The `path.Fragments()` call from the path also matches
  /// to effective duplicates of the geometry used for this object and therefore the paths of the other
  /// instances of this object.
  /// </summary>
  /// <param name="path"></param>
  /// <param name="fragmentStack"></param>
  private static void CollectFragments(InwOaPath path, Stack<InwOaFragment3> fragmentStack)
  {
    if (path.ArrayData is not Array identityPath)
    {
      return;
    }

    var identityPathArray = identityPath.ToArray<int>();
    int identityLength = identityPathArray.Length; // ← Cache once

    foreach (var fragment in path.Fragments().OfType<InwOaFragment3>())
    {
      if (ValidateFragmentPath(fragment, identityPathArray, identityLength))
      {
        fragmentStack.Push(fragment);
      }
    }
  }

  private List<Base> ProcessFragments(Stack<InwOaFragment3> fragmentStack, InwSelectionPathsColl paths)
  {
    var callbackListeners = new List<PrimitiveProcessor>();
    foreach (InwOaPath path in paths)
    {
      if (path.ArrayData is not Array pathData)
      {
        continue; // Skip paths without valid array data
      }

      var pathArray = pathData.ToArray<int>(); // ← Convert once per path
      int pathLength = pathArray.Length; // ← Cache length once per path

      var processor = new PrimitiveProcessor(_isUpright);
      foreach (var fragment in fragmentStack)
      {
        if (!ValidateFragmentPath(fragment, pathArray, pathLength)) // ← Use cached values
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
    var baseGeometries = ProcessGeometries(callbackListeners);
    return baseGeometries;
  }

  private static bool ValidateFragmentPath(InwOaFragment3 fragment, int[] identityPath, int identityLength) =>
    fragment.path?.ArrayData is Array fragmentPathData
    && IsSameFragmentPath(identityPath, identityLength, fragmentPathData);

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

  private static bool IsSameFragmentPath(int[] identityPath, int identityLength, Array fragmentPath)
  {
    if (identityLength != fragmentPath.Length)
    {
      return false;
    }

    var fragmentPathArray = fragmentPath.ToArray<int>();
    return identityPath.SequenceEqual(fragmentPathArray);
  }
}
