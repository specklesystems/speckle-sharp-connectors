using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Services;
using Speckle.Objects.Other;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MeshConversionToSpeckle : ITypedConverter<DB.Mesh, SOG.Mesh>
{
  private readonly IScalingServiceToSpeckle _toSpeckleScalingService;
  private readonly ITypedConverter<DB.Material, RenderMaterial> _materialConverter;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IRevitConversionContextStack _contextStack;

  public MeshConversionToSpeckle(
    IRevitConversionContextStack contextStack,
    IReferencePointConverter referencePointConverter,
    IScalingServiceToSpeckle toSpeckleScalingService,
    ITypedConverter<DB.Material, RenderMaterial> materialConverter
  )
  {
    _contextStack = contextStack;
    _toSpeckleScalingService = toSpeckleScalingService;
    _referencePointConverter = referencePointConverter;
    _materialConverter = materialConverter;
  }

  public SOG.Mesh Convert(DB.Mesh target)
  {
    var doc = _contextStack.Current.Document;

    List<double> vertices = GetSpeckleMeshVertexData(target);
    List<int> faces = GetSpeckleMeshFaceData(target);

    RenderMaterial? speckleMaterial = null;
    if (doc.GetElement(target.MaterialElementId) is DB.Material revitMaterial)
    {
      speckleMaterial = _materialConverter.Convert(revitMaterial);
    }

    return new SOG.Mesh(vertices, faces, units: _contextStack.Current.SpeckleUnits)
    {
      ["renderMaterial"] = speckleMaterial
    };
  }

  private List<double> GetSpeckleMeshVertexData(DB.Mesh target)
  {
    List<double> vertices = new(target.Vertices.Count * 3);

    foreach (DB.XYZ vert in target.Vertices)
    {
      // We need this method to take into account reference point transforms
      DB.XYZ extVert = _referencePointConverter.ConvertToExternalCoordinates(vert, true);

      vertices.Add(_toSpeckleScalingService.ScaleLength(extVert.X));
      vertices.Add(_toSpeckleScalingService.ScaleLength(extVert.Y));
      vertices.Add(_toSpeckleScalingService.ScaleLength(extVert.Z));
    }

    return vertices;
  }

  private List<int> GetSpeckleMeshFaceData(DB.Mesh target)
  {
    var faces = new List<int>(target.NumTriangles * 4);
    for (int i = 0; i < target.NumTriangles; i++)
    {
      var triangle = target.get_Triangle(i);
      faces.AddRange(GetMeshTriangleData(triangle));
    }

    return faces;
  }

  /// <summary>
  /// Retrieves the triangle data of a mesh to be stored in a Speckle Mesh faces property.
  /// </summary>
  /// <param name="triangle">The mesh triangle object.</param>
  /// <returns>A list of integers representing the triangle data.</returns>
  /// <remarks>
  /// Output format is a 4 item list with format [3, v1, v2, v3]; where the first item is the triangle flag (for speckle)
  /// and the 3 following numbers are the indices of each vertex in the vertex list.
  /// </remarks>
  private IReadOnlyList<int> GetMeshTriangleData(DB.MeshTriangle triangle) =>
    new[]
    {
      3, // The TRIANGLE flag in speckle
      (int)triangle.get_Index(0),
      (int)triangle.get_Index(1),
      (int)triangle.get_Index(2)
    };
}
