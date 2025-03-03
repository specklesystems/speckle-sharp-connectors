using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(ADB.Region), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class RegionToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Region, SOG.Mesh>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;

  public RegionToSpeckleConverter(ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter)
  {
    _brepConverter = brepConverter;
  }

  public Base Convert(object target) => Convert((ADB.Region)target);

  public SOG.Mesh Convert(ADB.Region target)
  {
    /*
    Autodesk.AutoCAD.DatabaseServices.DBObjectCollection curves = new();
    target.Explode(curves);
    Autodesk.AutoCAD.DatabaseServices.DBObjectCollection loops = target.GetRegionSection();

    for (int i = 0; i < loops.Count; i++)
    {
      ADB.Polyline? polyline = loops[i] as ADB.Polyline;
      if (polyline != null)
      {
        if (i == 0)
        {
          // Outer loop
          // Do something with the outer loop
        }
        else
        {
          // Inner loops
          // Do something with the inner loops
        }
      }
    }
    */

    using ABR.Brep brep = new(target);
    if (brep.IsNull)
    {
      throw new ConversionException("Could not retrieve brep from the region.");
    }

    SOG.Mesh mesh = _brepConverter.Convert(brep);
    mesh.area = target.Area;

    return mesh;
  }
}
