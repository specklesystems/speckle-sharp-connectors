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

  // Revit's strict short curve tolerance is approx 0.00256 feet. 
  // We use a slightly smaller tolerance for welding to ensure we don't collapse intentional small details,
  // but catch floating-point inaccuracies from other CAD software.
  private const double WELD_TOLERANCE = 1e-4;

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
    ForgeTypeId sourceUnitTypeId = _scalingService.UnitsToNative(speckleMesh.units);

    // 3. Process and Weld Vertices
    // Welding coincident vertices is critical because meshes often have unwelded vertices 
    // at UV seams, which Revit interprets as open edges (preventing Solid creation).
    var weldedVertices = new List<XYZ>();
    var vertexMap = new int[speckleMesh.vertices.Count / 3];

    for (int i = 0; i < speckleMesh.vertices.Count; i += 3)
    {
      var x = _scalingService.ScaleToNative(speckleMesh.vertices[i], sourceUnitTypeId);
      var y = _scalingService.ScaleToNative(speckleMesh.vertices[i + 1], sourceUnitTypeId);
      var z = _scalingService.ScaleToNative(speckleMesh.vertices[i + 2], sourceUnitTypeId);
      var pt = new XYZ(x, y, z);

      int matchedIndex = FindOrAddVertex(pt, weldedVertices, WELD_TOLERANCE);
      vertexMap[i / 3] = matchedIndex; // Map logical vertex index to welded vertex index
    }

    // 4. Configure Builder
    using var builder = new TessellatedShapeBuilder();
    builder.OpenConnectedFaceSet(true); // "true" means we prefer a Solid if closed

    // 5. Process Faces (Forcing Triangulation)
    // Our Mesh face format: [n, v1, v2, v3, ... , n, v1, v2, v3, v4, ...]
    int j = 0;
    while (j < speckleMesh.faces.Count)
    {
      int n = speckleMesh.faces[j];
      if (n < 3)
      {
        n += 3; // Legacy Speckle format: 0 -> 3 (triangle), 1 -> 4 (quad)
      }

      // Triangulate n-gons using a simple fan triangulation (assuming convex faces)
      // For a polygon with vertices v0, v1, v2, v3... we create triangles:
      // (v0, v1, v2), (v0, v2, v3), (v0, v3, v4), etc.
      int v0Index = speckleMesh.faces[j + 1];
      XYZ v0 = weldedVertices[vertexMap[v0Index]];

      for (int k = 2; k < n; k++)
      {
        int v1Index = speckleMesh.faces[j + k];
        int v2Index = speckleMesh.faces[j + k + 1];

        XYZ v1 = weldedVertices[vertexMap[v1Index]];
        XYZ v2 = weldedVertices[vertexMap[v2Index]];

        var triangleVertices = new List<XYZ> { v0, v1, v2 };

        // Ensure triangle is valid (not degenerate/zero-area) before passing to builder
        if (IsValidTriangle(triangleVertices))
        {
          try
          {
            builder.AddFace(new TessellatedFace(triangleVertices, ElementId.InvalidElementId));
          }
          catch (Autodesk.Revit.Exceptions.ArgumentException)
          {
            // Ignore highly degenerate triangles that squeaked past IsValidTriangle
          }
        }
      }

      j += n + 1; // Move to the next face block in the flat array
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
    return result.GetGeometricalObjects().FirstOrDefault();
  }

  /// <summary>
  /// Welds vertices by checking if a spatially coincident vertex already exists.
  /// </summary>
  private static int FindOrAddVertex(XYZ pt, List<XYZ> weldedVertices, double tolerance)
  {
    for (int i = 0; i < weldedVertices.Count; i++)
    {
      if (weldedVertices[i].IsAlmostEqualTo(pt, tolerance))
      {
        return i;
      }
    }
    weldedVertices.Add(pt);
    return weldedVertices.Count - 1;
  }

  /// <summary>
  /// Computes the area of a 3D triangle to ensure it is not degenerate.
  /// </summary>
  private static bool IsValidTriangle(List<XYZ> vertices)
  {
    if (vertices.Count != 3)
    {
      return false;
    }

    // Check for coincident adjacent vertices (collapsed edge)
    for (int i = 0; i < vertices.Count; i++)
    {
      var p1 = vertices[i];
      var p2 = vertices[(i + 1) % vertices.Count];
      if (p1.IsAlmostEqualTo(p2, WELD_TOLERANCE))
      {
        return false;
      }
    }

    // Calculate triangle area using cross product
    var v1 = vertices[1] - vertices[0];
    var v2 = vertices[2] - vertices[0];
    var crossProduct = v1.CrossProduct(v2);
    
    // The length of the cross product vector is twice the area of the triangle
    double area = crossProduct.GetLength() / 2.0;

    // If area is effectively zero, the face is degenerate
    return area > 1e-6;
  }
}
