using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.ToSpeckle.Meshing;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

[NameAndRankValue(typeof(RG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToSpeckleConverter : ITypedConverter<RG.Mesh, SOG.Mesh>
{
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public MeshToSpeckleConverter(
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Mesh to a Speckle Mesh.
  /// </summary>
  /// <param name="target">The Rhino Mesh to be converted.</param>
  /// <returns>The converted Speckle Mesh.</returns>
  /// <exception cref="Speckle.Sdk.Common.Exceptions.ValidationException">Thrown when the Rhino Mesh has 0 vertices or faces.</exception>
  public SOG.Mesh Convert(RG.Mesh target)
  {
    if (target.Vertices.Count == 0 || target.Faces.Count == 0)
    {
      throw new ValidationException("Cannot convert a mesh with 0 vertices/faces");
    }

    // 1. If needed, move geometry to origin before conversion
    RG.Mesh movedMesh = DisplayMeshExtractor.MoveToOriginAndGetDisplayMesh(
      target,
      _settingsStore.Current.ModelFarFromOrigin,
      out RG.Vector3d? vectorToGeometry
    );

    // 2. Convert extracted Mesh to Speckle. We don't move geometry back yet, because 'far from origin' geometry is causing Speckle conversion issues too
    SOG.Mesh convertedMesh = ConvertMesh(movedMesh);
    movedMesh.Dispose();

    // 3. Move Speckle geometry back from origin, if translation was applied
    DisplayMeshExtractor.MoveSpeckleMeshes([convertedMesh], vectorToGeometry, _settingsStore.Current.SpeckleUnits);
    return convertedMesh;
  }

  private SOG.Mesh ConvertMesh(RG.Mesh target)
  {
    var vertexCoordinates = new double[target.Vertices.Count * 3];
    var x = 0;
    for (int i = 0; i < target.Vertices.Count; i++)
    {
      var v = target.Vertices[i];
      vertexCoordinates[x++] = v.X;
      vertexCoordinates[x++] = v.Y;
      vertexCoordinates[x++] = v.Z;
    }

    List<int> faces = new();

    foreach (RG.MeshNgon polygon in target.GetNgonAndFacesEnumerable())
    {
      var vertIndices = polygon.BoundaryVertexIndexList();
      int n = vertIndices.Length;
      faces.Add(n);
      for (int i = 0; i < n; i++)
      {
        faces.Add((int)vertIndices[i]);
      }
    }

    var textureCoordinates = new double[target.TextureCoordinates.Count * 2];
    x = 0;
    foreach (var textureCoord in target.TextureCoordinates)
    {
      textureCoordinates[x++] = textureCoord.X;
      textureCoordinates[x++] = textureCoord.Y;
    }

    var colors = new int[target.VertexColors.Count];
    x = 0;
    foreach (var c in target.VertexColors)
    {
      colors[x++] = c.ToArgb();
    }

    var vertexNormals = new double[target.Normals.Count * 3];
    x = 0;
    foreach (var n in target.Normals)
    {
      vertexNormals[x++] = n.X;
      vertexNormals[x++] = n.Y;
      vertexNormals[x++] = n.Z;
    }

    double volume = target.IsClosed ? target.Volume() : 0;
    SOG.Box bbox = _boxConverter.Convert(new RG.Box(target.GetBoundingBox(false)));

    return new SOG.Mesh
    {
      vertices = new(vertexCoordinates),
      faces = faces,
      colors = new(colors),
      textureCoordinates = new(textureCoordinates),
      vertexNormals = new(vertexNormals),
      units = _settingsStore.Current.SpeckleUnits,
      volume = volume,
      bbox = bbox
    };
  }
}
