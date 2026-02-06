using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Rhino.Operations.Receive.Settings;

[GenerateAutoInterface]
public class ToHostSettingsManager : IToHostSettingsManager
{
  public bool GetConvertMeshesToBrepsSetting(ModelCard modelCard)
  {
    var value = modelCard.Settings?.FirstOrDefault(s => s.Id == ConvertMeshesToBrepsSetting.SETTING_ID)?.Value as bool?;
    return value is not null && value.NotNull();
  }
}
