using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MeshListConversionToSpeckle : ITypedConverter<List<DB.Mesh>, SOG.Mesh>
{
  private readonly IScalingServiceToSpeckle _toSpeckleScalingService;
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public MeshListConversionToSpeckle(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    IReferencePointConverter referencePointConverter,
    IScalingServiceToSpeckle toSpeckleScalingService
  )
  {
    _converterSettings = converterSettings;
    _toSpeckleScalingService = toSpeckleScalingService;
    _referencePointConverter = referencePointConverter;
  }

  public SOG.Mesh Convert(List<DB.Mesh> target)
  {
    // We compute the final size of the arrays to prevent unnecessary resizing.
    (int verticesSize, int facesSize) = GetVertexAndFaceListSize(target);

    Span<double> vertices = stackalloc double[verticesSize];
    var vertexCount = 0;
    Span<int> faces = stackalloc int[facesSize];
    var faceCount = 0;

    foreach (DB.Mesh mesh in target)
    {
      int faceIndexOffset = verticesSize / 3;

      foreach (DB.XYZ vert in mesh.Vertices)
      {
        // We need this method to take into account reference point transforms
        DB.XYZ extVert = _referencePointConverter.ConvertToExternalCoordinates(vert, true);

        vertices[vertexCount++] = _toSpeckleScalingService.ScaleLength(extVert.X);
        vertices[vertexCount++] = _toSpeckleScalingService.ScaleLength(extVert.Y);
        vertices[vertexCount++] = _toSpeckleScalingService.ScaleLength(extVert.Z);
      }

      for (int i = 0; i < mesh.NumTriangles; i++)
      {
        var triangle = mesh.get_Triangle(i);

        faces[faceCount++] = 3; // TRIANGLE flag
        faces[faceCount++] = (int)triangle.get_Index(0) + faceIndexOffset;
        faces[faceCount++] = (int)triangle.get_Index(1) + faceIndexOffset;
        faces[faceCount++] = (int)triangle.get_Index(2) + faceIndexOffset;
      }
    }

    SOG.Mesh speckleMesh =
      new()
      {
        vertices = new(vertices.ToArray()),
        faces = new(faces.ToArray()),
        units = _converterSettings.Current.SpeckleUnits
      };

    return speckleMesh;
  }

  private static (int vertexCount, int) GetVertexAndFaceListSize(IReadOnlyList<DB.Mesh?> meshes)
  {
    int numberOfVertices = 0;
    int numberOfFaces = 0;
    foreach (var mesh in meshes)
    {
      if (mesh == null)
      {
        continue;
      }

      numberOfVertices += mesh.Vertices.Count * 3;
      numberOfFaces += mesh.NumTriangles * 4;
    }

    return (numberOfVertices, numberOfFaces);
  }
}
