using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;

namespace Speckle.Converters.RevitShared.ToHost.Raw.Geometry;

/// <summary>
/// Converts a Speckle Mesh into a Revit GeometryObject (Solid or Mesh).
/// Specifically designed for Family creation (Freeform Elements) as it attempts to weld
/// vertices to form valid Solids, and ignores global project Reference Points.
/// </summary>
public class FreeformElementMeshToHostConverter : ITypedConverter<SOG.Mesh, GeometryObject>
{
  private readonly ScalingServiceToHost _scalingService;
  private readonly ILogger<FreeformElementMeshToHostConverter> _logger;

  private const double WELD_TOLERANCE = 1e-4;

  public FreeformElementMeshToHostConverter(
    ScalingServiceToHost scalingService,
    ILogger<FreeformElementMeshToHostConverter> logger
  )
  {
    _scalingService = scalingService;
    _logger = logger;
  }

  public GeometryObject Convert(SOG.Mesh speckleMesh)
  {
    if (speckleMesh.vertices.Count == 0 || speckleMesh.faces.Count == 0)
    {
      throw new ArgumentException("Mesh has no vertices or faces.", nameof(speckleMesh));
    }

    ForgeTypeId sourceUnitTypeId = _scalingService.UnitsToNative(speckleMesh.units);

    // Weld vertices
    var weldedVertices = new List<XYZ>();
    var vertexMap = new int[speckleMesh.vertices.Count / 3];

    for (int i = 0; i < speckleMesh.vertices.Count; i += 3)
    {
      var x = _scalingService.ScaleToNative(speckleMesh.vertices[i], sourceUnitTypeId);
      var y = _scalingService.ScaleToNative(speckleMesh.vertices[i + 1], sourceUnitTypeId);
      var z = _scalingService.ScaleToNative(speckleMesh.vertices[i + 2], sourceUnitTypeId);
      var pt = new XYZ(x, y, z);

      int matchedIndex = FindOrAddVertex(pt, weldedVertices, WELD_TOLERANCE);
      vertexMap[i / 3] = matchedIndex;
    }

    using var builder = new TessellatedShapeBuilder();
    builder.OpenConnectedFaceSet(true);

    int j = 0;
    while (j < speckleMesh.faces.Count)
    {
      int n = speckleMesh.faces[j];
      if (n < 3)
      {
        n += 3;
      }

      int v0Index = speckleMesh.faces[j + 1];
      XYZ v0 = weldedVertices[vertexMap[v0Index]];

      for (int k = 2; k < n; k++)
      {
        int v1Index = speckleMesh.faces[j + k];
        int v2Index = speckleMesh.faces[j + k + 1];

        XYZ v1 = weldedVertices[vertexMap[v1Index]];
        XYZ v2 = weldedVertices[vertexMap[v2Index]];

        var triangleVertices = new List<XYZ> { v0, v1, v2 };

        if (IsValidTriangle(triangleVertices))
        {
          try
          {
            builder.AddFace(new TessellatedFace(triangleVertices, ElementId.InvalidElementId));
          }
          catch (Autodesk.Revit.Exceptions.ArgumentException) { }
        }
      }

      j += n + 1;
    }

    builder.CloseConnectedFaceSet();

    try
    {
      builder.Target = TessellatedShapeBuilderTarget.AnyGeometry; // Attempts Solid
      builder.Fallback = TessellatedShapeBuilderFallback.Mesh;
      builder.Build();
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
    {
      _logger.LogError(ex, "TessellatedShapeBuilder failed to build geometry");
      throw new Speckle.Sdk.Common.Exceptions.ConversionException("Failed to build mesh geometry", ex);
    }

    var result = builder.GetBuildResult();

    if (result.Outcome == TessellatedShapeBuilderOutcome.Nothing)
    {
      throw new Speckle.Sdk.Common.Exceptions.ConversionException("TessellatedShapeBuilder produced no geometry.");
    }

    return result.GetGeometricalObjects().First();
  }

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

  private static bool IsValidTriangle(List<XYZ> vertices)
  {
    if (vertices.Count != 3)
    {
      return false;
    }

    for (int i = 0; i < vertices.Count; i++)
    {
      if (vertices[i].IsAlmostEqualTo(vertices[(i + 1) % vertices.Count], WELD_TOLERANCE))
      {
        return false;
      }
    }
    var crossProduct = (vertices[1] - vertices[0]).CrossProduct(vertices[2] - vertices[0]);
    return (crossProduct.GetLength() / 2.0) > 1e-6;
  }
}
