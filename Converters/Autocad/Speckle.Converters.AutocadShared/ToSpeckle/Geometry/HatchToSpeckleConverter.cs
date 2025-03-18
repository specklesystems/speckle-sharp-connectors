using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.Geometry;

[NameAndRankValue(typeof(ADB.Hatch), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchToSpeckleConverter : IToSpeckleTopLevelConverter, ITypedConverter<ADB.Hatch, SOG.Region>
{
  private readonly ITypedConverter<ABR.Brep, SOG.Mesh> _brepConverter;
  private readonly ITypedConverter<ADB.Region, SOG.Region> _regionConverter;

  public HatchToSpeckleConverter(
    ITypedConverter<ABR.Brep, SOG.Mesh> brepConverter,
    ITypedConverter<ADB.Region, SOG.Region> regionConverter
  )
  {
    _brepConverter = brepConverter;
    _regionConverter = regionConverter;
  }

  public Base Convert(object target) => Convert((ADB.Hatch)target);

  public SOG.Region Convert(ADB.Hatch target)
  {
    // generate Mesh for displayValue by converting to Regions first
    List<SOG.Region> regions = new();
    List<SOG.Mesh> displayValue = new();

    ADB.DBObjectCollection objCollection = new();
    target.Explode(objCollection);
    using (ADB.DBObjectCollection regionCollection = ADB.Region.CreateFromCurves(objCollection))
    {
      foreach (var region in regionCollection)
      {
        if (region is ADB.Region adbRegion)
        {
          using ABR.Brep brep = new(adbRegion);
          if (brep.IsNull)
          {
            throw new ConversionException("Could not retrieve brep from the hatch.");
          }
          // convert and store Meshes
          SOG.Mesh mesh = _brepConverter.Convert(brep);
          mesh.area = target.Area;
          displayValue.Add(mesh);

          // convert and store Regions
          SOG.Region convertedRegion = _regionConverter.Convert(adbRegion);
          convertedRegion.hasHatchPattern = true;
          regions.Add(convertedRegion);
        }
      }
    }

    return regions[0];
  }
}
