using Speckle.Converters.Common;

namespace Speckle.Converters.MicroStation.Settings;

public interface IMicroStationConversionSettingsFactory
{
  MicroStationConversionSettings Create(DPN.DgnModel activeModel, bool includeReferenceAttachments = true);
}

/// <summary>
/// Creates <see cref="MicroStationConversionSettings"/> from the given active model. The connector's
/// send binding resolves the active <see cref="DPN.DgnModel"/> (via <c>Session</c>) and passes it in,
/// so the converter project needs no reference to <c>ustation.dll</c>.
/// </summary>
public class MicroStationConversionSettingsFactory(IHostToSpeckleUnitConverter<DPN.UnitDefinition> unitConverter)
  : IMicroStationConversionSettingsFactory
{
  public MicroStationConversionSettings Create(DPN.DgnModel activeModel, bool includeReferenceAttachments = true)
  {
    DPN.ModelInfo info = activeModel.GetModelInfo();
    string speckleUnits = unitConverter.ConvertOrThrow(info.GetMasterUnit());
    BG.DPoint3d globalOrigin = info.GlobalOrigin;

    return new MicroStationConversionSettings(
      activeModel,
      speckleUnits,
      info.UorPerMaster,
      globalOrigin.X,
      globalOrigin.Y,
      globalOrigin.Z,
      includeReferenceAttachments
    );
  }
}
