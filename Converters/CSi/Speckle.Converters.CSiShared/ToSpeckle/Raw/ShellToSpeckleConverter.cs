using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Raw;

public class ShellToSpeckleConverter : ITypedConverter<CSiShellWrapper, Mesh>
{
  private readonly IConverterSettingsStore<CSiConversionSettings> _settingsStore;

  public ShellToSpeckleConverter(IConverterSettingsStore<CSiConversionSettings> settingsStore)
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
    List<double> vertices = new List<double>();
    List<int> faces = new List<int>();

    // How many vertices to define a face?
    faces.Add(numberPoints);

    // Lopp through points to get coordinates
    // TODO: This is gross!
    foreach (string pointName in pointNames)
    {
      double pointX = 0;
      double pointY = 0;
      double pointZ = 0;

      result = _settingsStore.Current.SapModel.PointObj.GetCoordCartesian(
        pointName,
        ref pointX,
        ref pointY,
        ref pointZ
      );

      if (result != 0)
      {
        throw new ArgumentException($"Failed to retrieve coordinate of vertex point name {pointName}.");
      }

      // Add vertex info
      vertices.Add(pointX);
      vertices.Add(pointY);
      vertices.Add(pointZ);

      // TODO: Check normals direction?
    }

    return new Mesh()
    {
      vertices = vertices,
      faces = faces,
      units = _settingsStore.Current.SpeckleUnits
    };
  }
}
