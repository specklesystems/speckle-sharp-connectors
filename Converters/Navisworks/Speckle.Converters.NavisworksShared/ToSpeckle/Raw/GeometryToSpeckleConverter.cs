using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Microsoft.Extensions.Logging;
using Speckle.Converter.Navisworks.Geometry;
using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Services;
using Speckle.Converter.Navisworks.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;
using ComApiBridge = Autodesk.Navisworks.Api.ComApi.ComApiBridge;

namespace Speckle.Converter.Navisworks.ToSpeckle;

[SuppressMessage("Design", "CA1031:Do not catch general exception types")]
public class GeometryToSpeckleConverter(
  NavisworksConversionSettings settings,
  InstanceStoreManager instanceStoreManager,
  ILogger<GeometryToSpeckleConverter> logger
)
{
  private readonly NavisworksConversionSettings _settings =
    settings ?? throw new ArgumentNullException(nameof(settings));

  private readonly bool _isUpright = settings.Derived.IsUpright;
  private readonly SafeVector _transformVector = settings.Derived.TransformVector;
  private const double SCALE = 1.0;

  private readonly InstanceStoreManager _instanceStoreManager =
    instanceStoreManager ?? throw new ArgumentNullException(nameof(instanceStoreManager));

  private readonly ILogger<GeometryToSpeckleConverter> _logger =
    logger ?? throw new ArgumentNullException(nameof(logger));

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
        if (paths.Count > 0)
        {
          var firstPath = paths.Cast<InwOaPath>().First();
          var fragmentsCollection = firstPath.Fragments();

          if (fragmentsCollection.Count > 1)
          {
            return ProcessSharedGeometry(paths, fragmentStack);
          }
        }

        foreach (InwOaPath path in paths)
        {
          CollectFragments(path, fragmentStack);
        }

        return ProcessFragments(fragmentStack, paths, true);
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
      if (AreFragmentPathsEqual(fragment, path))
      {
        fragmentStack.Push(fragment);
      }
    }
  }

  private List<Base> ProcessSharedGeometry(InwSelectionPathsColl paths, Stack<InwOaFragment3> fragmentStack)
  {
    var fragmentId = GenerateFragmentId(paths);

    if (string.IsNullOrEmpty(fragmentId))
    {
      foreach (InwOaPath path in paths)
      {
        CollectFragments(path, fragmentStack);
      }

      return ProcessFragments(fragmentStack, paths, true);
    }

    if (_instanceStoreManager.ContainsSharedGeometry(fragmentId))
    {
      return CreateInstanceReference(fragmentId, paths);
    }

    foreach (InwOaPath path in paths)
    {
      CollectFragments(path, fragmentStack);
    }

    var baseGeometry = ExtractUntransformedGeometry(fragmentStack);

    if (baseGeometry == null)
    {
      return ProcessFragments(fragmentStack, paths);
    }

    if (!_instanceStoreManager.AddSharedGeometry(fragmentId, baseGeometry))
    {
      return ProcessFragments(fragmentStack, paths);
    }

    return CreateInstanceReference(fragmentId, paths);
  }

  private List<Base> ProcessFragments(
    Stack<InwOaFragment3> fragmentStack,
    InwSelectionPathsColl paths,
    bool isSingleObject = false
  )
  {
    var callbackListeners = new List<PrimitiveProcessor>();

    foreach (InwOaPath path in paths)
    {
      var processor = new PrimitiveProcessor(_isUpright);

      foreach (var fragment in fragmentStack)
      {
        var matrix = fragment.GetLocalToWorldMatrix();
        var transform = matrix as InwLTransform3f3;
        if (transform?.Matrix is not Array matrixArray)
        {
          continue;
        }

        var fragmentCount = path.Fragments().Count;

        double[] makeNoChange = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
        double[] transformMatrix = ConvertArrayToDouble(matrixArray);

        if (isSingleObject || fragmentCount == 1)
        {
          processor.LocalToWorldTransformation = transformMatrix;
        }
        else
        {
          processor.LocalToWorldTransformation = makeNoChange;
        }

        fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
      }

      callbackListeners.Add(processor);
    }

    return ProcessGeometries(callbackListeners);
  }

  private static bool AreFragmentPathsEqual(InwOaFragment3 fragment, InwOaPath path) =>
    fragment.path?.ArrayData is Array fragmentPathData
    && path.ArrayData is Array pathData
    && AreFragmentPathsEqual(fragmentPathData, pathData);

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

      if (processor.Lines.Count > 0)
      {
        var lines = CreateLines(processor.Lines);
        baseGeometries.AddRange(lines);
      }
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
    lines
      .Select(line => new Line
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
      })
      .ToList();

  public string GenerateFragmentId(InwSelectionPathsColl paths)
  {
    try
    {
      if (paths.Count == 0)
      {
        return string.Empty;
      }

      var fragmentHashes = new List<string>();
      var pathIndex = 0;

      foreach (InwOaPath path in paths)
      {
        var fragments = path.Fragments();

        var fragmentIndex = 0;
        foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
        {
          if (fragment.path?.ArrayData is not Array pathData)
          {
            fragmentIndex++;
            continue;
          }

          if (pathData.Length == 0)
          {
            fragmentIndex++;
            continue;
          }

          try
          {
            if (pathData.Rank != 1)
            {
              var fragmentHashFallback = TrySimpleArrayEnumeration(pathData, fragmentIndex);
              if (!string.IsNullOrEmpty(fragmentHashFallback))
              {
                fragmentHashes.Add(fragmentHashFallback);
              }

              fragmentIndex++;
              continue;
            }

            var lowerBound = pathData.GetLowerBound(0);
            var upperBound = pathData.GetUpperBound(0);

            var arrayLength = upperBound - lowerBound + 1;
            var pathInts = new int[arrayLength];

            for (int i = lowerBound; i <= upperBound; i++)
            {
              try
              {
                var value = pathData.GetValue(i);
                var arrayIndex = i - lowerBound;
                pathInts[arrayIndex] = System.Convert.ToInt32(value);
              }
              catch (Exception ex)
              {
                _logger.LogDebug(ex, "Failed to get array value at COM index {Index}", i);
              }
            }

            var fragmentHash = string.Join("_", pathInts);
            fragmentHashes.Add(fragmentHash);
          }
          catch (Exception ex)
          {
            _logger.LogDebug(ex, "Failed to process fragment {FragmentIndex}, trying simple enumeration", fragmentIndex);
            var fragmentHash = TrySimpleArrayEnumeration(pathData, fragmentIndex);
            if (!string.IsNullOrEmpty(fragmentHash))
            {
              fragmentHashes.Add(fragmentHash);
            }

            fragmentIndex++;
            continue;
          }

          fragmentIndex++;
        }

        pathIndex++;
      }

      if (fragmentHashes.Count > 0)
      {
        fragmentHashes.Sort();
        var rawData = string.Join("__", fragmentHashes);
        var fragmentId = HashRawData(rawData);
        return fragmentId;
      }
      else
      {
        return string.Empty;
      }
    }
    catch (InvalidCastException ex)
    {
      _logger.LogWarning(ex, "Invalid cast when generating fragment ID");
      return string.Empty;
    }
    catch (IndexOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Array index out of range when generating fragment ID");
      return string.Empty;
    }
    catch (OverflowException ex)
    {
      _logger.LogWarning(ex, "Overflow when generating fragment ID");
      return string.Empty;
    }
    catch (ArgumentException ex)
    {
      _logger.LogWarning(ex, "Invalid argument when generating fragment ID");
      return string.Empty;
    }
    catch (COMException ex)
    {
      _logger.LogWarning(ex, "COM exception when generating fragment ID");
      return string.Empty;
    }
  }

  private string TrySimpleArrayEnumeration(Array pathData, int fragmentIndex)
  {
    try
    {
      var values = new List<string>();
      var maxAttempts = Math.Min(pathData.Length, 20);

      for (int i = 0; i < maxAttempts; i++)
      {
        try
        {
          var value = pathData.GetValue(i);
          var convertedValue = System.Convert.ToInt32(value);
          values.Add(convertedValue.ToString());
        }
        catch (IndexOutOfRangeException)
        {
          break;
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Failed to convert value at index {Index}", i);
        }
      }

      if (values.Count <= 0)
      {
        return string.Empty;
      }

      return string.Join("_", values);
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Simple enumeration failed for fragment {FragmentIndex}", fragmentIndex);
      return string.Empty;
    }
  }

  private static string HashRawData(string rawData)
  {
    using var sha256 = SHA256.Create();
    var inputBytes = Encoding.UTF8.GetBytes(rawData);
    var hashBytes = sha256.ComputeHash(inputBytes);
    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
  }

  private Base? ExtractUntransformedGeometry(Stack<InwOaFragment3> fragmentStack)
  {
    var processor = new PrimitiveProcessor(_isUpright);

    foreach (var fragment in fragmentStack)
    {
      double[] identityTransform = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
      processor.LocalToWorldTransformation = identityTransform;

      fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
    }

    return processor.Triangles.Count > 0 ? CreateMesh(processor.Triangles) : null;
  }

  private List<Base> CreateInstanceReference(string fragmentId, InwSelectionPathsColl paths)
  {
    var transform = ExtractInstanceTransform(paths);

    var instanceReference = new InstanceProxy
    {
      definitionId = $"def_{fragmentId}",
      transform = transform,
      units = _settings.Derived.SpeckleUnits,
      maxDepth = 0,
      applicationId = Guid.NewGuid().ToString()
    };

    return [instanceReference];
  }

  private Matrix4x4 ExtractInstanceTransform(InwSelectionPathsColl paths)
  {
    try
    {
      if (paths.Count == 0)
      {
        return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
      }

      var pathsEnum = paths.Cast<InwOaPath>();

      var firstPath = paths.Cast<InwOaPath>().First();
      var fragments = firstPath.Fragments();

      if (fragments.Count == 0)
      {
        return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
      }

      var fragmentStack = new Stack<InwOaFragment3>();

      foreach (var frag in fragments.OfType<InwOaFragment3>())
      {
        if (frag.path?.ArrayData is not Array pathData1 || firstPath.ArrayData is not Array pathData2)
        {
          continue;
        }

        var pathArray1 = pathData1.Cast<int>().ToArray<int>();
        var pathArray2 = pathData2.Cast<int>().ToArray<int>();

        if (pathArray1.Length == pathArray2.Length && pathArray1.SequenceEqual(pathArray2))
        {
          fragmentStack.Push(frag);
        }
      }

      var fragment = fragmentStack.First();
      var matrix = fragment.GetLocalToWorldMatrix();

      if (matrix is InwLTransform3f3 { Matrix: Array matrixArray })
      {
        var transformArray = ConvertArrayToDouble(matrixArray);
        var transformedMatrix = ApplyCoordinateTransform(transformArray);

        var newMatrix = new Matrix4x4(
          transformedMatrix[0],
          transformedMatrix[1],
          transformedMatrix[2],
          transformedMatrix[3],
          transformedMatrix[4],
          transformedMatrix[5],
          transformedMatrix[6],
          transformedMatrix[7],
          transformedMatrix[8],
          transformedMatrix[9],
          transformedMatrix[10],
          transformedMatrix[11],
          transformedMatrix[12],
          transformedMatrix[13],
          transformedMatrix[14],
          transformedMatrix[15]
        );

        return Matrix4x4.Transpose(newMatrix);
      }
    }
    catch (COMException ex)
    {
      _logger.LogWarning(ex, "COM object access failed while extracting instance transform");
    }
    catch (InvalidCastException ex)
    {
      _logger.LogWarning(ex, "Transform matrix cast failed");
    }
    catch (IndexOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Array access out of bounds");
    }
    catch (ArgumentException ex)
    {
      _logger.LogWarning(ex, "Invalid array dimensions");
    }
    catch (NullReferenceException ex)
    {
      _logger.LogWarning(ex, "Null fragment, matrix, or array reference");
    }

    return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
  }

  private double[] ApplyCoordinateTransform(double[] matrixArray)
  {
    var result = new double[16];
    Array.Copy(matrixArray, result, 16);

    result[12] = (result[12] + _transformVector.X) * SCALE;
    result[13] = (result[13] + _transformVector.Y) * SCALE;
    result[14] = (result[14] + _transformVector.Z) * SCALE;

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

  private static bool AreFragmentPathsEqual(Array a1, Array a2) =>
    a1.Length == a2.Length && a1.Cast<int>().SequenceEqual(a2.Cast<int>());
}
