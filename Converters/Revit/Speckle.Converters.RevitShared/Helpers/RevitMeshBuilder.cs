using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.RevitShared.Services;
using Mesh = Speckle.Objects.Geometry.Mesh;

namespace Speckle.Converters.RevitShared.Helpers;

/// <summary>
/// A stateless helper service to convert Speckle Meshes into Revit GeometryObjects (Solids or Meshes).
/// </summary>
public class RevitMeshBuilder
{
  private readonly ScalingServiceToHost _scalingService;
  private readonly ILogger<RevitMeshBuilder> _logger;

  public RevitMeshBuilder(ScalingServiceToHost scalingService, ILogger<RevitMeshBuilder> logger)
  {
    _scalingService = scalingService;
    _logger = logger;
  }

  /// <summary>
  /// Builds a Revit <see cref="GeometryObject"/> (Solid or Mesh) from a Speckle Mesh.
  /// Uses TessellatedShapeBuilder with a fallback to "Mesh" if the geometry is not watertight.
  /// </summary>
  /// <returns>A valid Revit GeometryObject, or null if building failed.</returns>
  public GeometryObject? BuildFreeformElementGeometry(Mesh speckleMesh)
  {
    // 1. Validate Input
    if (speckleMesh.vertices.Count == 0 || speckleMesh.faces.Count == 0)
    {
      return null;
    }

    // 2. Pre-calculate Unit
    // We get the ForgeTypeId once to avoid parsing the string for every vertex.
    ForgeTypeId sourceUnitTypeId = _scalingService.UnitsToNative(speckleMesh.units);

    // 3. Process Vertices
    // Convert all vertices to Revit Internal Units (Feet)
    var revitVertices = new List<XYZ>(speckleMesh.vertices.Count / 3);
    for (int i = 0; i < speckleMesh.vertices.Count; i += 3)
    {
      var x = _scalingService.ScaleToNative(speckleMesh.vertices[i], sourceUnitTypeId);
      var y = _scalingService.ScaleToNative(speckleMesh.vertices[i + 1], sourceUnitTypeId);
      var z = _scalingService.ScaleToNative(speckleMesh.vertices[i + 2], sourceUnitTypeId);
      revitVertices.Add(new XYZ(x, y, z));
    }

    // 4. Configure Builder
    using var builder = new TessellatedShapeBuilder();
    builder.OpenConnectedFaceSet(true); // "true" means we prefer a Solid if closed

    // 5. Build Faces
    // Our Mesh face format: [n, v1, v2, v3, ... , n, v1, v2, v3, v4, ...]
    // Don't think I need to check array bounds here - we assure on publish
    int j = 0;
    while (j < speckleMesh.faces.Count)
    {
      int n = speckleMesh.faces[j];
      if (n < 3)
      {
        n += 3; // 0 -> 3 (triangle), 1 -> 4 (quad)
      }

      var faceVertices = new List<XYZ>(n);
      for (int k = 1; k <= n; k++)
      {
        int vertIndex = speckleMesh.faces[j + k];
        faceVertices.Add(revitVertices[vertIndex]);
      }

      // Add face to builder
      // Wrapped in try-catch because degenerate faces (collinear points, tiny area) cause AddFace to throw
      // We want to skip bad faces and keep the good ones (?)
      try
      {
        // Note: invalidating the material ID (ElementId.InvalidElementId) for now
        // Material assignment happens on the FreeFormElement wrapper later.
        builder.AddFace(new TessellatedFace(faceVertices, ElementId.InvalidElementId));
      }
      catch (Autodesk.Revit.Exceptions.ArgumentException)
      {
        // Common error: "The points are almost coincident" or "The loop is degenerate"
        // We implicitly ignore these faces.
      }

      j += n + 1;
    }

    builder.CloseConnectedFaceSet();

    // 6. Build Result
    // Target.AnyGeometry attempts to build a Solid. If the mesh is not closed (watertight),
    // it automatically falls back to a Mesh (Open Shell).
    try
    {
      builder.Target = TessellatedShapeBuilderTarget.AnyGeometry;
      builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
      builder.Build();
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
    {
      _logger.LogError(ex, "TessellatedShapeBuilder failed to build geometry");
      return null;
    }

    var result = builder.GetBuildResult();

    if (result.Outcome == TessellatedShapeBuilderOutcome.Nothing)
    {
      _logger.LogWarning("TessellatedShapeBuilder produced no geometry");
      return null;
    }

    // Return the primary object (Solid or Mesh)
    // There should typically be only one object in the result set for a connected face set.
    return result.GetGeometricalObjects().FirstOrDefault();
  }
}
