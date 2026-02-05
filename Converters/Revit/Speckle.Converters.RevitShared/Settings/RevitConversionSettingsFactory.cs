using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.Settings;

[GenerateAutoInterface]
public class RevitConversionSettingsFactory(
  RevitContext revitContext,
  IHostToSpeckleUnitConverter<DB.ForgeTypeId> unitConverter
) : IRevitConversionSettingsFactory
{
  public RevitConversionSettings Create(
    DetailLevelType detailLevelType,
    DB.Transform? referencePointTransform,
    bool sendEmptyOrNullParams,
    bool sendLinkedModels,
    bool sendRebarsAsVolumetric,
    bool sendAreasAsMesh,
    bool receiveInstancesAsFamilies,
    double tolerance = 0.0164042 // 5mm in ft
  )
  {
    var document = revitContext.UIApplication.NotNull().ActiveUIDocument.Document;
    return new(
      document,
      detailLevelType,
      referencePointTransform,
      unitConverter.ConvertOrThrow(document.GetUnits().GetFormatOptions(DB.SpecTypeId.Length).GetUnitTypeId()),
      sendEmptyOrNullParams,
      sendLinkedModels,
      sendRebarsAsVolumetric,
      sendAreasAsMesh,
      receiveInstancesAsFamilies,
      tolerance
    );
  }
}
