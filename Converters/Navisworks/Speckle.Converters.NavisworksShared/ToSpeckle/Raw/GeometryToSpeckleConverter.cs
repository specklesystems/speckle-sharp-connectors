using System.Collections.Concurrent;
using System.Runtime.InteropServices;
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
  private const double SCALE = 1.0; // Default scale factor

  private readonly InstanceStoreManager _instanceStoreManager =
    instanceStoreManager ?? throw new ArgumentNullException(nameof(instanceStoreManager));

  private readonly ILogger<GeometryToSpeckleConverter> _logger =
    logger ?? throw new ArgumentNullException(nameof(logger));

  // Fragment ID cache for performance optimization
  private readonly ConcurrentDictionary<int, string> _fragmentIdCache = new();

  // Geometry cache for repeated items
  private readonly ConcurrentDictionary<string, List<Base>> _geometryCache = new();

  /// <summary>
  /// Clears all internal caches. Should be called when starting a new conversion session.
  /// </summary>
  public void ClearCaches()
  {
    _fragmentIdCache.Clear();
    _geometryCache.Clear();
  }

  /// <summary>
  /// Gets cache statistics for performance monitoring.
  /// </summary>
  /// <returns>A record containing cache hit counts and sizes</returns>
  public (int FragmentIdCacheSize, int GeometryCacheSize, double CacheMemoryEstimateMB) GetCacheStatistics()
  {
    var fragmentCacheSize = _fragmentIdCache.Count;
    var geometryCacheSize = _geometryCache.Count;

    // Rough memory estimate (fragment IDs ~50 bytes, geometry objects ~10KB average)
    var estimatedMemoryMb = (fragmentCacheSize * 50 + geometryCacheSize * 10240) / (1024.0 * 1024.0);

    return (fragmentCacheSize, geometryCacheSize, Math.Round(estimatedMemoryMb, 2));
  }

  /// <summary>
  /// Converts a ModelItem's geometry to Speckle display geometry by accessing the underlying COM objects.
  /// When path.Fragments().Count > 1, extracts untransformed base geometry once, stores in SharedGeometryStore,
  /// and returns instance references. Otherwise, returns transformed geometry directly.
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

    // Check geometry cache first
    var itemId = modelItem.InstanceGuid.ToString();
    if (_geometryCache.TryGetValue(itemId, out var cachedGeometry))
    {
      return cachedGeometry;
    }

    using var comSelection = new ComScope<InwOpSelection>(ComApiBridge.ToInwOpSelection([modelItem]));
    var fragmentStack = new Stack<InwOaFragment3>();

    using var paths = new ComScope<InwSelectionPathsColl>(comSelection.Value.Paths());

    try
    {
      // Check if this geometry is shared across multiple instances
      List<Base> result;
      if (paths.Value.Count > 0)
      {
        var firstPath = paths.Value.Cast<InwOaPath>().First();
        var fragmentsCollection = firstPath.Fragments();

        if (fragmentsCollection.Count > 1)
        {
          // Shared geometry - extract base geometry once and return instance reference
          result = ProcessSharedGeometry(paths.Value, fragmentStack);
        }
        else
        {
          // Single instance geometry - process normally with transforms
          foreach (InwOaPath path in paths.Value)
          {
            CollectFragments(path, fragmentStack);
          }

          result = ProcessFragments(fragmentStack, paths.Value, true);
        }
      }
      else
      {
        result = [];
      }

      // Cache the result for future use
      if (result.Count > 0)
      {
        _geometryCache.TryAdd(itemId, result);
      }

      return result;
    }
    catch (COMException ex)
    {
      _logger.LogError(ex, "COM exception converting geometry for ModelItem {ItemId}", itemId);
      return [];
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "Invalid operation converting geometry for ModelItem {ItemId}", itemId);
      return [];
    }
  }

  private static void CollectFragments(InwOaPath path, Stack<InwOaFragment3> fragmentStack)
  {
    using var fragments = new ComScope<InwNodeFragsColl>(path.Fragments());

    foreach (var fragment in fragments.Value.OfType<InwOaFragment3>())
    {
      if (ValidateFragmentPath(fragment, path))
      {
        fragmentStack.Push(fragment);
      }
    }
  }

  private List<Base> ProcessSharedGeometry(InwSelectionPathsColl paths, Stack<InwOaFragment3> fragmentStack)
  {
    // Generate ID from fragment data for shared geometry
    var fragmentId = GenerateFragmentId(paths);

    if (string.IsNullOrEmpty(fragmentId))
    {
      // Fallback to normal processing if we can't generate ID
      foreach (InwOaPath path in paths)
      {
        CollectFragments(path, fragmentStack);
      }

      return ProcessFragments(fragmentStack, paths, true);
    }

    // Check if shared geometry already exists in store
    if (_instanceStoreManager.ContainsSharedGeometry(fragmentId))
    {
      // Return instance reference to existing geometry
      return CreateInstanceReference(fragmentId, paths);
    }

    // Extract untransformed base geometry
    foreach (InwOaPath path in paths)
    {
      CollectFragments(path, fragmentStack);
    }

    var baseGeometry = ExtractUntransformedGeometry(fragmentStack);

    if (baseGeometry == null)
    {
      return ProcessFragments(fragmentStack, paths);
    }

    // Store both the geometry definition and create the instance definition proxy
    if (!_instanceStoreManager.AddSharedGeometry(fragmentId, baseGeometry))
    {
      return ProcessFragments(fragmentStack, paths);
    }

    // Return instance reference to the newly stored geometry
    return CreateInstanceReference(fragmentId, paths);

    // Fallback to normal processing if store failed
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

      using var pathFragments = new ComScope<InwNodeFragsColl>(path.Fragments());
      var fragmentCount = pathFragments.Value.Count;

      foreach (var fragment in fragmentStack)
      {
        try
        {
          var matrix = fragment.GetLocalToWorldMatrix();
          var transform = matrix as InwLTransform3f3;
          if (transform?.Matrix is not Array matrixArray)
          {
            continue;
          }

          double[] makeNoChange = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
          double[] transformMatrix = ConvertArrayToDouble(matrixArray);

          if (isSingleObject || fragmentCount == 1)
          {
            // Apply coordinate system transformation
            processor.LocalToWorldTransformation = transformMatrix;
          }
          else
          {
            // For multiple objects, process geometry without transforms
            processor.LocalToWorldTransformation = makeNoChange;
          }

          fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
        }
        catch (COMException ex)
        {
          _logger.LogWarning(ex, "COM exception processing fragment, skipping");
        }
      }

      callbackListeners.Add(processor);
    }

    return ProcessGeometries(callbackListeners);
  }

  private static bool ValidateFragmentPath(InwOaFragment3 fragment, InwOaPath path) =>
    fragment.path?.ArrayData is Array fragmentPathData
    && path.ArrayData is Array pathData
    && IsSameFragmentPath(fragmentPathData, pathData);

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

  /// <summary>
  /// Generates an idempotent ID from fragment path data for shared geometry.
  /// Uses the path.Fragments() collection to create a reproducible hash.
  /// </summary>
  public string GenerateFragmentId(InwSelectionPathsColl paths)
  {
    try
    {
      if (paths.Count == 0)
      {
        return string.Empty;
      }

      // Generate a fast hash code for cache lookup
      var pathsHashCode = GenerateFastPathsHashCode(paths);

      // Check cache first
      if (_fragmentIdCache.TryGetValue(pathsHashCode, out var cachedId))
      {
        return cachedId;
      }

      var fragmentHashes = new List<string>();

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
            // Check array rank first - COM arrays might be multidimensional
            if (pathData.Rank != 1)
            {
              // Try simple enumeration fallback
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
              catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException)
              {
                // Skip invalid array values
              }
            }

            var fragmentHash = string.Join("_", pathInts);
            fragmentHashes.Add(fragmentHash);
          }
          catch (Exception ex) when (ex is InvalidCastException or IndexOutOfRangeException or RankException)
          {
            // Try simple enumeration as fallback
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
      }

      string fragmentId;
      if (fragmentHashes.Count > 0)
      {
        // Sort to ensure consistent ordering
        fragmentHashes.Sort();
        var rawData = string.Join("__", fragmentHashes);
        fragmentId = HashRawData(rawData);
      }
      else
      {
        fragmentId = string.Empty;
      }

      // Cache the result for future use
      if (!string.IsNullOrEmpty(fragmentId))
      {
        _fragmentIdCache.TryAdd(pathsHashCode, fragmentId);
      }

      return fragmentId;
    }
    catch (Exception ex)
      when (ex
          is InvalidCastException
            or IndexOutOfRangeException
            or OverflowException
            or ArgumentException
            or COMException
      )
    {
      _logger.LogWarning(ex, "Failed to generate fragment ID due to {ExceptionType}", ex.GetType().Name);
      return string.Empty;
    }
  }

  /// <summary>
  /// Simple array enumeration fallback when bounds access fails.
  /// Tries to enumerate array by simple sequential access.
  /// </summary>
  private string TrySimpleArrayEnumeration(Array pathData, int fragmentIndex)
  {
    try
    {
      var values = new List<string>();
      var maxAttempts = Math.Min(pathData.Length, 20); // Limit attempts to avoid infinite loops

      for (int i = 0; i < maxAttempts; i++)
      {
        try
        {
          var value = pathData.GetValue(i);
          var convertedValue = System.Convert.ToInt32(value);
          values.Add(convertedValue.ToString());
          _logger.LogDebug("Fragment {FragmentIndex} simple enum[{Index}] = {Value}", fragmentIndex, i, convertedValue);
        }
        catch (IndexOutOfRangeException)
        {
          // Hit the end of valid indices
          _logger.LogDebug("Fragment {FragmentIndex} reached end of array at index {Index}", fragmentIndex, i);
          break;
        }
        catch (Exception ex)
          when (ex is InvalidCastException or OverflowException or FormatException or ArgumentException)
        {
          _logger.LogWarning(ex, "Fragment {FragmentIndex} failed to convert value at index {Index}", fragmentIndex, i);
        }
      }

      if (values.Count <= 0)
      {
        return string.Empty;
      }

      var hash = string.Join("_", values);
      return hash;
    }
    catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
    {
      _logger.LogWarning(ex, "Simple enumeration failed for fragment {FragmentIndex}", fragmentIndex);
      return string.Empty;
    }
  }

  /// <summary>
  /// Generates a fast hash code for paths collection for caching purposes
  /// </summary>
  private static int GenerateFastPathsHashCode(InwSelectionPathsColl paths)
  {
    unchecked
    {
      int hash = 17;
      hash = hash * 23 + paths.Count;

      var processed = 0;
      foreach (InwOaPath path in paths)
      {
        if (path.ArrayData is Array { Length: > 0 } pathData)
        {
          // Sample first few elements for performance
          var sampleSize = Math.Min(pathData.Length, 4);
          for (int i = 0; i < sampleSize; i++)
          {
            hash = hash * 23 + (pathData.GetValue(i)?.GetHashCode() ?? 0);
          }

          hash = hash * 23 + pathData.Length;
        }

        // Limit processing for performance
        if (++processed >= 8)
        {
          break;
        }
      }

      return hash;
    }
  }

  /// <summary>
  /// Creates a fast hash of the raw fragment data using .NET's HashCode struct.
  /// For performance, we use HashCode instead of SHA256 for fragment IDs.
  /// </summary>
  /// <returns>Hash as hex string</returns>
  private static string HashRawData(string rawData)
  {
    var hashCode = rawData.GetHashCode();
    return hashCode.ToString("X8");
  }

  /// <summary>
  /// Extracts untransformed base geometry from fragments.
  /// This geometry will be stored once and referenced by instances.
  /// </summary>
  private Base? ExtractUntransformedGeometry(Stack<InwOaFragment3> fragmentStack)
  {
    var processor = new PrimitiveProcessor(_isUpright);

    // Process fragments without transforms to get base geometry
    foreach (var fragment in fragmentStack)
    {
      // Use identity transform to get untransformed geometry
      double[] identityTransform = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
      processor.LocalToWorldTransformation = identityTransform;

      fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
    }

    // Create mesh from untransformed geometry
    return processor.Triangles.Count > 0 ? CreateMesh(processor.Triangles) : null;
  }

  /// <summary>
  /// Creates an instance reference to shared geometry stored in the InstanceStoreManager.
  /// This is returned instead of full geometry for shared instances.
  /// </summary>
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

  /// <summary>
  /// Extracts the transform matrix from the first path's fragments for instance placement.
  /// </summary>
  private Matrix4x4 ExtractInstanceTransform(InwSelectionPathsColl paths)
  {
    try
    {
      if (paths.Count == 0)
      {
        return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
      }

      var firstPath = paths.Cast<InwOaPath>().First();
      using var fragments = new ComScope<InwNodeFragsColl>(firstPath.Fragments());

      if (fragments.Value.Count == 0)
      {
        return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
      }

      var fragmentStack = new Stack<InwOaFragment3>();
      // Get the first fragment's transform matrix
      foreach (var frag in fragments.Value.OfType<InwOaFragment3>())
      {
        try
        {
          if (frag.path?.ArrayData is not Array pathData1 || firstPath.ArrayData is not Array pathData2)
          {
            continue;
          }

          // Use IsSameFragmentPath for consistency and performance
          if (IsSameFragmentPath(pathData1, pathData2))
          {
            fragmentStack.Push(frag);
          }
        }
        catch (COMException ex)
        {
          _logger.LogWarning(ex, "COM exception accessing fragment path data, skipping fragment");
        }
      }

      if (fragmentStack.Count == 0)
      {
        _logger.LogWarning("No valid fragments found for transform extraction");
        return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
      }

      var fragment = fragmentStack.First();
      var matrix = fragment.GetLocalToWorldMatrix();

      if (matrix is InwLTransform3f3 { Matrix: Array matrixArray })
      {
        var transformArray = ConvertArrayToDouble(matrixArray);

        // Apply coordinate system transformation
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
    catch (Exception ex)
      when (ex
          is COMException
            or InvalidCastException
            or IndexOutOfRangeException
            or ArgumentException
            or NullReferenceException
      )
    {
      _logger.LogWarning(
        ex,
        "Failed to extract instance transform ({ExceptionType}) - returning identity matrix",
        ex.GetType().Name
      );
    }

    return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
  }

  private double[] ApplyCoordinateTransform(double[] matrixArray)
  {
    // Apply scale and coordinate transformation
    var result = new double[16];
    Array.Copy(matrixArray, result, 16);

    // Apply translation transformation
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

  private static bool IsSameFragmentPath(Array a1, Array a2) =>
    a1.Length == a2.Length
    && (
      a1.Length > 4
        ? a1.Cast<object>().SequenceEqual(a2.Cast<object>())
        : !a1.Cast<object>().Where((_, i) => !Equals(a1.GetValue(i), a2.GetValue(i))).Any()
    );
}
