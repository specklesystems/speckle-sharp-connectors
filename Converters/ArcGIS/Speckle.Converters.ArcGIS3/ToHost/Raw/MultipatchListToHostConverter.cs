using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;

namespace Speckle.Converters.ArcGIS3.ToHost.Raw;

public class MultipatchListToHostConverter : ITypedConverter<List<SGIS.GisMultipatchGeometry>, ACG.Multipatch>
{
  private readonly ITypedConverter<SOG.Point, ACG.MapPoint> _pointConverter;

  public MultipatchListToHostConverter(ITypedConverter<SOG.Point, ACG.MapPoint> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public ACG.Multipatch Convert(List<SGIS.GisMultipatchGeometry> target)
  {
    if (target.Count == 0)
    {
      throw new SpeckleConversionException("Feature contains no geometries");
    }
    ACG.MultipatchBuilderEx multipatchPart = new();
    foreach (SGIS.GisMultipatchGeometry part in target)
    {
      ACG.Patch newPatch = multipatchPart.MakePatch(ACG.PatchType.Triangles);
      for (int i = 0; i < part.vertices.Count / 3; i++)
      {
        newPatch.AddPoint(
          _pointConverter.Convert(
            new SOG.Point(part.vertices[i * 3], part.vertices[i * 3 + 1], part.vertices[i * 3 + 2])
          )
        );
      }
      multipatchPart.Patches.Add(newPatch);
    }
    return multipatchPart.ToGeometry();
  }
}
