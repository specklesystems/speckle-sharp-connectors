using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.RevitShared.Settings;

[GenerateAutoInterface]
public class RevitConversionSettingsFactory(IHostToSpeckleUnitConverter<DB.ForgeTypeId> unitConverter)
  : IRevitConversionSettingsFactory
{
  public RevitConversionSettings Create(
    DB.Document document,
    DetailLevelType detailLevelType,
    DB.Transform? referencePointTransform,
    double tolerance = 0.0164042 // 5mm in ft
  ) =>
    new()
    {
      Document = document,
      DetailLevel = detailLevelType,
      ReferencePointTransform = referencePointTransform,
      Tolerance = tolerance,
      SpeckleUnits = unitConverter.ConvertOrThrow(
        document.GetUnits().GetFormatOptions(DB.SpecTypeId.Length).GetUnitTypeId()
      )
    };
}
