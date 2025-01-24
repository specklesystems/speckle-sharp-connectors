using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.RevitShared.Settings;

[GenerateAutoInterface]
public class RevitConversionSettingsFactory(
  IRevitContext revitContext,
  IHostToSpeckleUnitConverter<DB.ForgeTypeId> unitConverter
) : IRevitConversionSettingsFactory
{
  public RevitConversionSettings Create(
    DetailLevelType detailLevelType,
    DB.Transform? referencePointTransform,
    bool sendEmptyOrNullParams,
    double tolerance = 0.0164042 // 5mm in ft
  )
  {
    var document = revitContext.UIApplication.ActiveUIDocument.Document;
    return new(
      document,
      detailLevelType,
      referencePointTransform,
      unitConverter.ConvertOrThrow(document.GetUnits().GetFormatOptions(DB.SpecTypeId.Length).GetUnitTypeId()),
      sendEmptyOrNullParams,
      tolerance
    );
  }
}
