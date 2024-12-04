using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.CSiShared.ToSpeckle.Geometry;

/// <summary>
/// Every shell has as its displayValue a planar 2D Speckle mesh defined by the vertices.
/// </summary>
/// <remarks>
/// Creates a mesh from shell vertices using the CSi API:
/// 1. Gets shell vertex point names
/// 2. Extracts coordinates for each vertex
/// 3. Constructs mesh using flat vertex list (x,y,z triplets) and face indices
/// 
/// TODO: Implement point caching and weak referencing to joint objects for better performance
/// TODO: Investigate if SAP2000 has other freeform non-planar surface definitions?
/// The TODOs noted will be completed as part of the "Data Extraction (Send)" milestone.
/// 
/// Face indices format:
/// - First value is the number of vertices in the face
/// - Followed by indices into the vertex list
/// 
/// Throws ArgumentException if vertex extraction fails.
/// </remarks>
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
