using Rhino.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

[NameAndRankValue(typeof(RG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToSpeckleConverter : ITypedConverter<RG.Mesh, SOG.Mesh>
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public MeshToSpeckleConverter(IConverterSettingsStore<RhinoConversionSettings> settingsStore)
  {
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

    SOG.Mesh convertedMesh = ConvertMesh(target);

    return convertedMesh;
  }

  // Rhino common is casting mesh vertex coords from doubles to float: by default the api returns `Vertices` as float instead of double precision
  // https://github.com/mcneel/rhino3dm/blob/71c63a8c1c87782a13a1b76c825e4b792b36fd09/src/dotnet/opennurbs/opennurbs_mesh.cs#L6990-L7000
  // We need to use double precision or else meshes far from origin will come out distorted: do *not* access `Vertices` directly - use `ToPoint3dArray`
  private double[] ConvertDoublePrecisionVertices(RG.Mesh target)
  {
    var vertexCoordinates = new double[target.Vertices.Count * 3];
    RG.Point3d[] vertices = target.Vertices.ToPoint3dArray();
    var x = 0;
    for (int i = 0; i < vertices.Length; i++)
    {
      var v = vertices[i];
      vertexCoordinates[x++] = v.X;
      vertexCoordinates[x++] = v.Y;
      vertexCoordinates[x++] = v.Z;
    }

    return vertexCoordinates;
  }

  private SOG.Mesh ConvertMesh(RG.Mesh target)
  {
    var vertexCoordinates = ConvertDoublePrecisionVertices(target);

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

    var colors = new int[target.VertexColors.Count];
    int x = 0;
    foreach (var c in target.VertexColors)
    {
      colors[x++] = c.ToArgb();
    }

    // NOTE: textureCoordinates and vertexNormals will be empty array when setting is false
    double[] textureCoordinates = [];
    double[] vertexNormals = [];
    if (_settingsStore.Current.AddVisualizationProperties)
    {
      textureCoordinates = new double[target.TextureCoordinates.Count * 2];
      x = 0;
      foreach (var textureCoord in target.TextureCoordinates)
      {
        textureCoordinates[x++] = textureCoord.X;
        textureCoordinates[x++] = textureCoord.Y;
      }

      vertexNormals = new double[target.Normals.Count * 3];
      x = 0;
      foreach (var n in target.Normals)
      {
        vertexNormals[x++] = n.X;
        vertexNormals[x++] = n.Y;
        vertexNormals[x++] = n.Z;
      }
    }

    // get area and volume props
    double area = AreaMassProperties.Compute(target).Area;
    double volume = target.IsClosed ? target.Volume() : 0;

    return new SOG.Mesh
    {
      vertices = [.. vertexCoordinates],
      faces = faces,
      colors = [.. colors],
      textureCoordinates = [.. textureCoordinates], // this will be empty array when setting is false
      vertexNormals = [.. vertexNormals], // this will be empty array when setting is false
      units = _settingsStore.Current.SpeckleUnits,
      volume = volume,
      area = area,
    };
  }
}
