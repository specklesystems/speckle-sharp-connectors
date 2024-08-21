using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.ToSpeckle;
using Speckle.Objects;

namespace Speckle.Converters.Revit2023.ToSpeckle.TopLevel;

[NameAndRankValue(nameof(DB.Grid), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public sealed class GridTopLevelConverterToSpeckle : BaseTopLevelConverterToSpeckle<DB.Grid, SOBE.GridLine>
{
  private readonly ITypedConverter<DB.Curve, ICurve> _curveConverter;
  private readonly IRevitConversionContextStack _contextStack;

  public GridTopLevelConverterToSpeckle(
    ITypedConverter<DB.Curve, ICurve> curveConverter,
    IRevitConversionContextStack contextStack
  )
  {
    _curveConverter = curveConverter;
    _contextStack = contextStack;
  }

  public override SOBE.GridLine Convert(DB.Grid target) =>
    new()
    {
      baseLine = _curveConverter.Convert(target.Curve),
      label = target.Name,
      applicationId = target.UniqueId,
      units = _contextStack.Current.SpeckleUnits
    };
}
