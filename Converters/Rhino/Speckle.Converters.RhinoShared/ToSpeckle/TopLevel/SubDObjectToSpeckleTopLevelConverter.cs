using Speckle.Converters.Common;
using Rhino.DocObjects;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(SubDObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class SubDObjectToSpeckleTopLevelConverter: IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Brep, SOG.Brep> _brepConverter;

  public SubDObjectToSpeckleTopLevelConverter(ITypedConverter<RG.Brep, SOG.Brep> curveConverter)
  {
    _brepConverter = curveConverter;
  }

  public Base Convert(object target)
  {
    var subDObject = (SubDObject)target;
    var subD = (RG.SubD)subDObject.Geometry;
    var speckleCurve = _brepConverter.Convert(subD.ToBrep());
    return speckleCurve;
  }
}
