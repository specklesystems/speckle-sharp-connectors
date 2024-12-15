using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Grasshopper.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Point), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class Point3dToHostTopLevelConverter : IToHostTopLevelConverter
{
  private readonly ITypedConverter<SOG.Point, RG.Point3d> _pointConverter;

  public Point3dToHostTopLevelConverter(ITypedConverter<SOG.Point, RG.Point3d> pointConverter)
  {
    _pointConverter = pointConverter;
  }

  public object Convert(Base target) => Convert((SOG.Point)target);

  public RG.Point3d Convert(SOG.Point target) => _pointConverter.Convert(target);
}
