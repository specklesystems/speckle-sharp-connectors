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
  private const double SCALE = 1.0; // Default scale factor

  private readonly InstanceStoreManager _instanceStoreManager =
    instanceStoreManager ?? throw new ArgumentNullException(nameof(instanceStoreManager));

  private readonly ILogger<GeometryToSpeckleConverter> _logger =
    logger ?? throw new ArgumentNullException(nameof(logger));

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

    var comSelection = ComApiBridge.ToInwOpSelection([modelItem]);
    try
    {
      var fragmentStack = new Stack<InwOaFragment3>();
      var paths = comSelection.Paths();
      try
      {
        // Check if this geometry is shared across multiple instances
        if (paths.Count > 0)
        {
          var firstPath = paths.Cast<InwOaPath>().First();
          var fragmentsCollection = firstPath.Fragments();

          _logger.LogDebug(
            "Instancing check: PathCount={PathCount}, FragmentCount={FragmentCount}",
            paths.Count,
            fragmentsCollection.Count
          );

          if (fragmentsCollection.Count > 1)
          {
            _logger.LogDebug("Detected shared geometry - processing as instanced");
            // Shared geometry - extract base geometry once and return instance reference
            return ProcessSharedGeometry(paths, fragmentStack);
          }
          else
          {
            _logger.LogDebug("Single fragment detected - processing as regular geometry");
          }
        }

        // Single instance geometry - process normally with transforms
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

  private static void CollectFragments(InwOaPath path, Stack<InwOaFragment3> fragmentStack)
  {
    var fragments = path.Fragments();

    foreach (var fragment in fragments.OfType<InwOaFragment3>())
    {
      if (ValidateFragmentPath(fragment, path))
      {
        fragmentStack.Push(fragment);
      }
    }
  }

  private List<Base> ProcessSharedGeometry(InwSelectionPathsColl paths, Stack<InwOaFragment3> fragmentStack)
  {
    _logger.LogDebug("ProcessSharedGeometry called with {PathCount} paths", paths.Count);

    // Generate ID from fragment data for shared geometry
    var fragmentId = GenerateFragmentId(paths);

    if (string.IsNullOrEmpty(fragmentId))
    {
      _logger.LogWarning(
        "Could not generate fragment ID from {PathCount} paths with {FragmentCount} total fragments - falling back to normal geometry processing",
        paths.Count,
        paths.Cast<InwOaPath>().Sum(p => p.Fragments().Count)
      );

      // Debug the paths collection to understand why ID generation failed
      DebugPathsCollection(paths, "ProcessSharedGeometry - ID generation failed");
      // Fallback to normal processing if we can't generate ID
      foreach (InwOaPath path in paths)
      {
        CollectFragments(path, fragmentStack);
      }

      return ProcessFragments(fragmentStack, paths);
    }

    // Check if shared geometry already exists in store
    if (_instanceStoreManager.ContainsSharedGeometry(fragmentId))
    {
      _logger.LogDebug(
        "Reusing existing shared geometry: Fragment={FragmentId}, Definition={DefinitionId}",
        fragmentId,
        $"def_{fragmentId}"
      );
      // Return instance reference to existing geometry
      return CreateInstanceReference(fragmentId, paths);
    }

    _logger.LogDebug("Creating new shared geometry with FragmentId={FragmentId}", fragmentId);

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
      _logger.LogWarning("Failed to store shared geometry for FragmentId={FragmentId}", fragmentId);
      return ProcessFragments(fragmentStack, paths);
    }

    _logger.LogDebug(
      "Successfully stored shared geometry: Fragment={FragmentId}, Geometry={GeometryId}, Definition={DefinitionId}",
      fragmentId,
      $"geom_{fragmentId}",
      $"def_{fragmentId}"
    );
    // Return instance reference to the newly stored geometry
    return CreateInstanceReference(fragmentId, paths);

    // Fallback to normal processing if store failed
  }

  private List<Base> ProcessFragments(Stack<InwOaFragment3> fragmentStack, InwSelectionPathsColl paths)
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

        double[] makeNoChange = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

        processor.LocalToWorldTransformation =
          path.Fragments().Count == 1 ? makeNoChange : ConvertArrayToDouble(matrixArray);

        fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);
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
  private string GenerateFragmentId(InwSelectionPathsColl paths)
  {
    try
    {
      _logger.LogDebug("Starting fragment ID generation from {PathCount} paths", paths.Count);

      if (paths.Count == 0)
      {
        _logger.LogDebug("No paths available for fragment ID generation");
        return string.Empty;
      }

      var fragmentHashes = new List<string>();
      var pathIndex = 0;

      foreach (InwOaPath path in paths)
      {
        _logger.LogDebug("Processing path {PathIndex}", pathIndex);

        var fragments = path.Fragments();
        _logger.LogDebug("Path {PathIndex} has {FragmentCount} fragments", pathIndex, fragments.Count);

        var fragmentIndex = 0;
        foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
        {
          _logger.LogDebug("Processing fragment {FragmentIndex} in path {PathIndex}", fragmentIndex, pathIndex);

          if (fragment.path?.ArrayData is not Array pathData)
          {
            _logger.LogDebug("Fragment {FragmentIndex} has no path data, skipping", fragmentIndex);
            fragmentIndex++;
            continue;
          }

          _logger.LogDebug("Fragment {FragmentIndex} path data length: {Length}", fragmentIndex, pathData.Length);

          if (pathData.Length == 0)
          {
            _logger.LogDebug("Fragment {FragmentIndex} has empty path data, skipping", fragmentIndex);
            fragmentIndex++;
            continue;
          }

          try
          {
            // Check array rank first - COM arrays might be multi-dimensional
            if (pathData.Rank != 1)
            {
              _logger.LogDebug(
                "Fragment {FragmentIndex} has multi-dimensional array (Rank={Rank}), trying simple enumeration",
                fragmentIndex,
                pathData.Rank
              );
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

            _logger.LogDebug(
              "Fragment {FragmentIndex} array bounds: LowerBound={Lower}, UpperBound={Upper}, Rank={Rank}",
              fragmentIndex,
              lowerBound,
              upperBound,
              pathData.Rank
            );

            var arrayLength = upperBound - lowerBound + 1;
            var pathInts = new int[arrayLength];

            for (int i = lowerBound; i <= upperBound; i++)
            {
              try
              {
                var value = pathData.GetValue(i);
                var arrayIndex = i - lowerBound;
                pathInts[arrayIndex] = System.Convert.ToInt32(value);
                _logger.LogDebug(
                  "Fragment {FragmentIndex} path[{ArrayIndex}] = {Value} (COM index: {ComIndex})",
                  fragmentIndex,
                  arrayIndex,
                  value,
                  i
                );
              }
              catch (Exception ex)
              {
                _logger.LogDebug(ex, "Failed to get array value at COM index {Index}, skipping", i);
                continue;
              }
            }

            var fragmentHash = string.Join("_", pathInts);
            _logger.LogDebug("Fragment {FragmentIndex} raw hash: {Hash}", fragmentIndex, fragmentHash);
            fragmentHashes.Add(fragmentHash);
          }
          catch (Exception ex)
          {
            _logger.LogDebug(
              ex,
              "Failed to process fragment {FragmentIndex} with bounds access, trying simple enumeration",
              fragmentIndex
            );
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

        pathIndex++;
      }

      _logger.LogDebug("Collected {HashCount} fragment hashes total", fragmentHashes.Count);

      if (fragmentHashes.Count > 0)
      {
        // Sort to ensure consistent ordering
        fragmentHashes.Sort();
        var rawData = string.Join("__", fragmentHashes);
        var fragmentId = HashRawData(rawData);
        _logger.LogDebug(
          "Generated fragment ID: {FragmentId} (SHA256 of shared_geometry_{RawData}) from {HashCount} fragment hashes",
          fragmentId,
          rawData,
          fragmentHashes.Count
        );
        return fragmentId;
      }
      else
      {
        _logger.LogDebug("No valid fragment hashes collected, returning empty string");
        return string.Empty;
      }
    }
    catch (InvalidCastException ex)
    {
      _logger.LogWarning(ex, "Invalid cast when generating fragment ID - fragment path data type unexpected");
      return string.Empty;
    }
    catch (IndexOutOfRangeException ex)
    {
      _logger.LogWarning(ex, "Array index out of range when generating fragment ID - path data structure unexpected");
      return string.Empty;
    }
    catch (OverflowException ex)
    {
      _logger.LogWarning(ex, "Overflow when generating fragment ID - path data values too large");
      return string.Empty;
    }
    catch (ArgumentException ex)
    {
      _logger.LogWarning(ex, "Invalid argument when generating fragment ID - array or string operations failed");
      return string.Empty;
    }
    catch (COMException ex)
    {
      _logger.LogWarning(ex, "COM exception when generating fragment ID - fragment access failed");
      return string.Empty;
    }
  }

  /// <summary>
  /// Fallback method for fragment ID generation using alternative fragment properties.
  /// </summary>
  private string GenerateFragmentIdFallback(InwSelectionPathsColl paths)
  {
    try
    {
      var fallbackHashes = new List<string>();

      foreach (InwOaPath path in paths)
      {
        var fragments = path.Fragments();

        foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
        {
          // Try using fragment's internal ID or hash code as fallback
          var fragmentHashCode = fragment.GetHashCode().ToString();
          fallbackHashes.Add(fragmentHashCode);

          _logger.LogDebug("Fallback: Using fragment hash code: {HashCode}", fragmentHashCode);
        }
      }

      if (fallbackHashes.Count > 0)
      {
        fallbackHashes.Sort();
        var rawData = string.Join("_", fallbackHashes);
        var fallbackId = HashRawData(rawData);
        _logger.LogDebug(
          "Generated fallback fragment ID: {FallbackId} (SHA256 of fallback_geometry_{RawData})",
          fallbackId,
          rawData
        );
        return fallbackId;
      }

      // Try geometry-based approach as final fallback
      _logger.LogDebug("Fallback fragment ID generation also failed, trying geometry-based approach");
      return GenerateGeometryBasedFragmentId(paths);
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Fallback fragment ID generation failed");
      // Try geometry-based approach as final fallback
      return GenerateGeometryBasedFragmentId(paths);
    }
  }

  /// <summary>
  /// Final fallback: Generate ID based on geometry characteristics when fragment paths fail.
  /// Uses vertex counts, triangle counts, and bounding box data to create a semi-stable ID.
  /// </summary>
  private string GenerateGeometryBasedFragmentId(InwSelectionPathsColl paths)
  {
    try
    {
      var geometryHashes = new List<string>();

      foreach (InwOaPath path in paths)
      {
        var fragments = path.Fragments();

        foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
        {
          // Create a processor to analyze the geometry characteristics
          var processor = new PrimitiveProcessor(_isUpright);
          double[] identityTransform = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
          processor.LocalToWorldTransformation = identityTransform;

          try
          {
            fragment.GenerateSimplePrimitives(nwEVertexProperty.eNORMAL, processor);

            // Create a hash based on geometry characteristics
            var triangleCount = processor.Triangles.Count;
            var lineCount = processor.Lines.Count;
            var pointCount = processor.Points.Count;

            // Get bounding box if possible
            var minX =
              processor.Triangles.Count > 0
                ? processor.Triangles.Min(t => Math.Min(Math.Min(t.Vertex1.X, t.Vertex2.X), t.Vertex3.X))
                : 0;
            var maxX =
              processor.Triangles.Count > 0
                ? processor.Triangles.Max(t => Math.Max(Math.Max(t.Vertex1.X, t.Vertex2.X), t.Vertex3.X))
                : 0;

            var geometrySignature =
              $"{triangleCount}t_{lineCount}l_{pointCount}p_{(int)(minX * 1000)}_{(int)(maxX * 1000)}";
            geometryHashes.Add(geometrySignature);

            _logger.LogDebug(
              "Geometry-based hash: {Hash} (T:{TriangleCount}, L:{LineCount}, P:{PointCount})",
              geometrySignature,
              triangleCount,
              lineCount,
              pointCount
            );
          }
          catch (Exception ex)
          {
            _logger.LogDebug(ex, "Could not analyze fragment geometry for ID generation");
            // Use a generic hash for this fragment
            geometryHashes.Add($"frag_{fragment.GetHashCode()}");
          }
        }
      }

      if (geometryHashes.Count > 0)
      {
        geometryHashes.Sort();
        var rawData = string.Join("_", geometryHashes);
        var geometryId = HashRawData(rawData);
        _logger.LogDebug(
          "Generated geometry-based fragment ID: {GeometryId} (SHA256 of geometry_based_{RawData})",
          geometryId,
          rawData
        );
        return geometryId;
      }

      return string.Empty;
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Geometry-based fragment ID generation failed");
      return string.Empty;
    }
  }

  /// <summary>
  /// Debug helper to log detailed information about paths collection structure.
  /// </summary>
  private void DebugPathsCollection(InwSelectionPathsColl paths, string context)
  {
    try
    {
      _logger.LogDebug("=== Debugging paths collection for {Context} ===", context);
      _logger.LogDebug("Paths collection count: {Count}", paths.Count);

      var pathIndex = 0;
      foreach (InwOaPath path in paths)
      {
        _logger.LogDebug("Path {Index}:", pathIndex);

        try
        {
          var fragments = path.Fragments();
          _logger.LogDebug("  - Fragment count: {Count}", fragments.Count);

          var fragmentIndex = 0;
          foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
          {
            _logger.LogDebug("  - Fragment {Index}:", fragmentIndex);
            _logger.LogDebug("    - Fragment hash code: {HashCode}", fragment.GetHashCode());

            if (fragment.path?.ArrayData is Array pathData)
            {
              _logger.LogDebug("    - Path data length: {Length}", pathData.Length);
              _logger.LogDebug(
                "    - Array bounds: LowerBound={Lower}, UpperBound={Upper}, Rank={Rank}",
                pathData.GetLowerBound(0),
                pathData.GetUpperBound(0),
                pathData.Rank
              );

              var arrayLength = pathData.GetUpperBound(0) - pathData.GetLowerBound(0) + 1;
              if (arrayLength <= 10) // Only log small arrays to avoid spam
              {
                var values = new List<object>();
                for (int i = pathData.GetLowerBound(0); i <= pathData.GetUpperBound(0); i++)
                {
                  try
                  {
                    values.Add(pathData.GetValue(i));
                  }
                  catch (Exception ex)
                  {
                    values.Add($"ERROR_at_{i}: {ex.Message}");
                  }
                }

                _logger.LogDebug("    - Path data values: [{Values}]", string.Join(", ", values));
              }
              else
              {
                _logger.LogDebug("    - Path data too large to log (array length: {Length})", arrayLength);
              }
            }
            else
            {
              _logger.LogDebug("    - No path data available");
            }

            fragmentIndex++;
          }
        }
        catch (Exception ex)
        {
          _logger.LogDebug(ex, "Error analyzing path {Index}", pathIndex);
        }

        pathIndex++;
      }

      _logger.LogDebug("=== End paths collection debug ===");
    }
    catch (Exception ex)
    {
      _logger.LogWarning(ex, "Failed to debug paths collection for {Context}", context);
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

      _logger.LogDebug(
        "Fragment {FragmentIndex} trying simple enumeration (max {MaxAttempts} attempts)",
        fragmentIndex,
        maxAttempts
      );

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
        {
          _logger.LogDebug(ex, "Fragment {FragmentIndex} failed to convert value at index {Index}", fragmentIndex, i);
          continue;
        }
      }

      if (values.Count > 0)
      {
        var hash = string.Join("_", values);
        _logger.LogDebug("Fragment {FragmentIndex} simple enumeration raw hash: {Hash}", fragmentIndex, hash);
        return hash;
      }

      return string.Empty;
    }
    catch (Exception ex)
    {
      _logger.LogDebug(ex, "Fragment {FragmentIndex} simple enumeration completely failed", fragmentIndex);
      return string.Empty;
    }
  }

  /// <summary>
  /// Creates a SHA256 hash of the raw fragment data to ensure consistent, secure identifiers.
  /// </summary>
  /// <param name="rawData">The raw fragment data to hash (without prefix)</param>
  /// <returns>SHA256 hash as lowercase hex string (64 characters)</returns>
  private static string HashRawData(string rawData)
  {
    using var sha256 = SHA256.Create();
    var inputBytes = Encoding.UTF8.GetBytes(rawData);
    var hashBytes = sha256.ComputeHash(inputBytes);
    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
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
      maxDepth = 0
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
      var fragments = firstPath.Fragments();

      if (fragments.Count == 0)
      {
        return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
      }

      // Get the first fragment's transform matrix
      var fragment = fragments.OfType<InwOaFragment3>().First();
      var matrix = fragment.GetLocalToWorldMatrix();

      if (matrix is InwLTransform3f3 { Matrix: Array matrixArray })
      {
        var transformArray = ConvertArrayToDouble(matrixArray);

        // Apply coordinate system transformation
        var transformedMatrix = ApplyCoordinateTransform(transformArray);

        return new Matrix4x4(
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
      }
    }
    catch (COMException ex)
    {
      _logger.LogWarning(
        ex,
        "COM object access failed while extracting instance transform - returning identity matrix"
      );
    }
    catch (InvalidCastException ex)
    {
      _logger.LogWarning(ex, "Transform matrix cast failed (not a valid InwLTransform3f3) - returning identity matrix");
    }
    catch (IndexOutOfRangeException ex)
    {
      _logger.LogWarning(
        ex,
        "Array access out of bounds - matrix array structure unexpected - returning identity matrix"
      );
    }
    catch (ArgumentException ex)
    {
      _logger.LogWarning(ex, "Invalid array dimensions or other argument issues - returning identity matrix");
    }
    catch (NullReferenceException ex)
    {
      _logger.LogWarning(ex, "Null fragment, matrix, or array reference - returning identity matrix");
    }

    return new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1);
  }

  /// <summary>
  /// Applies coordinate system transformation to the matrix array.
  /// </summary>
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
    a1.Length == a2.Length && a1.Cast<int>().SequenceEqual(a2.Cast<int>());
}
