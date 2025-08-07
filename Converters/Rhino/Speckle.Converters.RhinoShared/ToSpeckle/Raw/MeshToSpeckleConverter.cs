using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Rhino.Extensions;
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

    // Extracting Rhino Mesh and converting to Speckle with the most suitable settings (e.g. moving to origin first, if needed)
    // This is needed because of Rhino using single precision numbers for Mesh vertices: https://wiki.mcneel.com/rhino/farfromorigin
    RG.Mesh meshToConvert = target;
    RG.Vector3d? vector = null;

    // 1. If needed, move geometry to origin
    if (_settingsStore.Current.ModelFarFromOrigin && target.IsFarFromOrigin(out RG.Vector3d vectorToGeometry))
    {
      meshToConvert = (RG.Mesh)target.Duplicate();
      meshToConvert.Transform(RG.Transform.Translation(-vectorToGeometry));
      vector = vectorToGeometry;
    }
    // 2. Convert extracted Mesh to Speckle. We don't move geometry back yet, because 'far from origin' geometry is causing Speckle conversion issues too
    SOG.Mesh convertedMesh = ConvertMesh(meshToConvert);

    // 3. Move Speckle geometry back from origin, if translation was applied
    DisplayMeshExtractor.MoveSpeckleMeshes([convertedMesh], vector, _settingsStore.Current.SpeckleUnits);

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

    // TODO: Should sendVertexNormals be extended to textureCoordinates, too?
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

    // NOTE: vertexNormals will be empty array when setting is false
    double[] vertexNormals = [];
    if (_settingsStore.Current.SendVertexNormals)
    {
      vertexNormals = new double[target.Normals.Count * 3];
      x = 0;
      foreach (var n in target.Normals)
      {
        vertexNormals[x++] = n.X;
        vertexNormals[x++] = n.Y;
        vertexNormals[x++] = n.Z;
      }
    }

    double volume = target.IsClosed ? target.Volume() : 0;
    SOG.Box bbox = _boxConverter.Convert(new RG.Box(target.GetBoundingBox(false)));

    return new SOG.Mesh
    {
      vertices = [.. vertexCoordinates],
      faces = faces,
      colors = [.. colors],
      textureCoordinates = [.. textureCoordinates],
      vertexNormals = [.. vertexNormals], // This will be empty array when setting is false
      units = _settingsStore.Current.SpeckleUnits,
      volume = volume,
      bbox = bbox
    };
  }
}
