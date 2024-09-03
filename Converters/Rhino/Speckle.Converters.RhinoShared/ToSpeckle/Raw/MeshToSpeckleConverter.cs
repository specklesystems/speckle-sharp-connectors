using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.Rhino.ToSpeckle.Raw;

[NameAndRankValue(nameof(RG.Mesh), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class MeshToSpeckleConverter : ITypedConverter<RG.Mesh, SOG.Mesh>
{
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Box, SOG.Box> _boxConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public MeshToSpeckleConverter(
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Box, SOG.Box> boxConverter,
    IConverterSettingsStore<RhinoConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _boxConverter = boxConverter;
    _settingsStore = settingsStore;
  }

  /// <summary>
  /// Converts a Rhino Mesh to a Speckle Mesh.
  /// </summary>
  /// <param name="target">The Rhino Mesh to be converted.</param>
  /// <returns>The converted Speckle Mesh.</returns>
  /// <exception cref="SpeckleConversionException">Thrown when the Rhino Mesh has 0 vertices or faces.</exception>
  public SOG.Mesh Convert(RG.Mesh target)
  {
    if (target.Vertices.Count == 0 || target.Faces.Count == 0)
    {
      throw new SpeckleConversionException("Cannot convert a mesh with 0 vertices/faces");
    }

    List<double> vertexCoordinates = new(target.Vertices.Count * 3);
    foreach (var v in target.Vertices)
    {
      vertexCoordinates.Add(v.X);
      vertexCoordinates.Add(v.Y);
      vertexCoordinates.Add(v.Z);
    }

    List<int> faces = new();
    foreach (RG.MeshNgon polygon in target.GetNgonAndFacesEnumerable())
    {
      var vertIndices = polygon.BoundaryVertexIndexList();
      int n = vertIndices.Length;
      faces.Add(n);
      faces.AddRange(vertIndices.Select(vertIndex => (int)vertIndex));
    }

    List<double> textureCoordinates = new(target.TextureCoordinates.Count * 2);
    foreach (var textureCoord in target.TextureCoordinates)
    {
      textureCoordinates.Add(textureCoord.X);
      textureCoordinates.Add(textureCoord.Y);
    }

    List<int> colors = new(target.VertexColors.Count);
    foreach (var c in target.VertexColors)
    {
      colors.Add(c.ToArgb());
    }

    double volume = target.IsClosed ? target.Volume() : 0;
    SOG.Box bbox = _boxConverter.Convert(new RG.Box(target.GetBoundingBox(false)));

    return new SOG.Mesh(vertexCoordinates, faces, colors, textureCoordinates, _settingsStore.Current.SpeckleUnits)
    {
      volume = volume,
      bbox = bbox
    };
  }
}
