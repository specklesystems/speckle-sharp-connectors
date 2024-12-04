using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Geometry;

// NOTE: This is HORRIBLE but serves just as a poc! We need point caching and weak referencing to joint objects
public class MeshToSpeckleConverter : ITypedConverter<CSiShellWrapper, Mesh>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;

  public MeshToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public Mesh Convert(CSiShellWrapper target)
  {
    int numberPoints = 0;
    string[] pointNames = Array.Empty<string>();
    int result = _settingsStore.Current.SapModel.AreaObj.GetPoints(target.Name, ref numberPoints, ref pointNames);

    if (result != 0)
    {
      throw new ArgumentException($"Failed to convert {target.Name} to Speckle Mesh");
    }

    // List to store vertices defining a face
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
