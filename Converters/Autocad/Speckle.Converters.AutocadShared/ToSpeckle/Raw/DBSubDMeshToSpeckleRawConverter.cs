using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBSubDMeshToSpeckleRawConverter : ITypedConverter<ADB.SubDMesh, SOG.Mesh>
{
  private readonly IReferencePointConverter _referencePointConverter;
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBSubDMeshToSpeckleRawConverter(
    IReferencePointConverter referencePointConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _referencePointConverter = referencePointConverter;
    _settingsStore = settingsStore;
  }

  public SOG.Mesh Convert(ADB.SubDMesh target)
  {
    // vertices
    List<double> vertices = new(target.Vertices.Count * 3);
    foreach (AG.Point3d vert in target.Vertices)
    {
      vertices.Add(vert.X);
      vertices.Add(vert.Y);
      vertices.Add(vert.Z);
    }

    // faces
    List<int> faces = new();
    int[] faceArr = target.FaceArray.ToArray(); // contains vertex indices
    int edgeCount = 0;
    for (int i = 0; i < faceArr.Length; i = i + edgeCount + 1)
    {
      List<int> faceVertices = new();
      edgeCount = faceArr[i];
      for (int j = i + 1; j <= i + edgeCount; j++)
      {
        faceVertices.Add(faceArr[j]);
      }

      if (edgeCount == 4) // quad face
      {
        faces.AddRange(new List<int> { 4, faceVertices[0], faceVertices[1], faceVertices[2], faceVertices[3] });
      }
      else // triangle face
      {
        faces.AddRange(new List<int> { 3, faceVertices[0], faceVertices[1], faceVertices[2] });
      }
    }

    // colors
    var colors = target
      .VertexColorArray.Select(o =>
        System
          .Drawing.Color.FromArgb(
            System.Convert.ToInt32(o.Red),
            System.Convert.ToInt32(o.Green),
            System.Convert.ToInt32(o.Blue)
          )
          .ToArgb()
      )
      .ToList();

    SOG.Mesh speckleMesh =
      new()
      {
        vertices = _referencePointConverter.ConvertWCSDoublesToExternalCoordinates(vertices), // transform with reference point
        faces = faces,
        colors = colors,
        units = _settingsStore.Current.SpeckleUnits,
        area = target.ComputeSurfaceArea()
      };

    try
    {
      speckleMesh.volume = target.ComputeVolume();
    }
    catch (Exception e) when (!e.IsFatal()) { } // for non-volumetric meshes

    return speckleMesh;
  }
}
