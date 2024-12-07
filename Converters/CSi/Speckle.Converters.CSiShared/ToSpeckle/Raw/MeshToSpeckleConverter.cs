using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

/// <summary>
/// Every shell has as its displayValue a planar 2D Speckle mesh. This is defined by the vertices.
/// </summary>
/// <remarks>
/// Display value extraction is always handled by CsiShared.
/// This is because geometry representation is the same for both Sap2000 and Etabs products.
/// TODO: Point caching and weak referencing to joint objects for better performance
/// TODO: Investigate if SAP2000 has other freeform non-planar surface definitions?
/// </remarks>
public class MeshToSpeckleConverter : ITypedConverter<CsiShellWrapper, Mesh>
{
  private readonly IConverterSettingsStore<CsiConversionSettings> _settingsStore;

  public MeshToSpeckleConverter(IConverterSettingsStore<CsiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Mesh Convert(CsiShellWrapper target)
  {
    int numberPoints = 0;
    string[] pointNames = Array.Empty<string>();
    int result = _settingsStore.Current.SapModel.AreaObj.GetPoints(target.Name, ref numberPoints, ref pointNames);

    if (result != 0)
    {
      throw new ArgumentException($"Failed to convert {target.Name} to Speckle Mesh");
    }

    // NOTE: Face indices format:
    // - First value is the number of vertices in the face
    // - Followed by indices into the vertex list
    List<double> vertices = new List<double>(numberPoints * 3);
    List<int> faces = new List<int>(numberPoints + 1);

    for (int i = 0; i < numberPoints; i++)
    {
      double pointX = 0;
      double pointY = 0;
      double pointZ = 0;

      result = _settingsStore.Current.SapModel.PointObj.GetCoordCartesian(
        pointNames[i],
        ref pointX,
        ref pointY,
        ref pointZ
      );

      if (result != 0)
      {
        throw new ArgumentException($"Failed to retrieve coordinate of vertex point name {pointNames[i]}.");
      }

      vertices.Add(pointX);
      vertices.Add(pointY);
      vertices.Add(pointZ);
    }

    faces.Add(numberPoints);
    for (int i = 0; i < numberPoints; i++)
    {
      faces.Add(i);
    }

    return new Mesh()
    {
      vertices = vertices,
      faces = faces,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
