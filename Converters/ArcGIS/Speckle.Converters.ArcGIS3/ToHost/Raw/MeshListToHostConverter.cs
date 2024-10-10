using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Utils;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class MeshListToHostConverter : ITypedConverter<List<SOG.Mesh>, ACG.Multipatch>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;
  private readonly IConverterSettingsStore<ArcGISConversionSettings> _settingsStore;

  public MeshListToHostConverter(
    ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter,
    IConverterSettingsStore<ArcGISConversionSettings> settingsStore
  )
  {
    _pointConverter = pointConverter;
    _settingsStore = settingsStore;
  }

  public ACG.Multipatch Convert(List<SOG.Mesh> target)
  {
    if (target.Count == 0)
    {
      throw new ValidationException("Feature contains no geometries");
    }
    ACG.MultipatchBuilderEx multipatchPart = new(_settingsStore.Current.ActiveCRSoffsetRotation.SpatialReference);

    foreach (SOG.Mesh part in target)
    {
      part.TriangulateMesh();
      ACG.Patch newPatch = multipatchPart.MakePatch(ACG.PatchType.Triangles);
      for (int i = 0; i < part.faces.Count; i++)
      {
        if (i % 4 == 0)
        {
          continue;
        }
        int ptIndex = part.faces[i];
        newPatch.AddPoint(
          _pointConverter.Convert(
            new SOG.Point(
              part.vertices[ptIndex * 3],
              part.vertices[ptIndex * 3 + 1],
              part.vertices[ptIndex * 3 + 2],
              part.units
            )
          )
        );
      }
      multipatchPart.Patches.Add(newPatch);
    }
    return multipatchPart.ToGeometry();
  }
}
