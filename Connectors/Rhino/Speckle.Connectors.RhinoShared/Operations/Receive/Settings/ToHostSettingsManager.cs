using Speckle.Connectors.DUI.Models.Card;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Rhino.Operations.Receive.Settings;

[GenerateAutoInterface]
public class ToHostSettingsManager : IToHostSettingsManager
{
  public bool GetConvertMeshesToPolysurfacesSetting(ModelCard modelCard)
  {
    var value =
      modelCard.Settings?.FirstOrDefault(s => s.Id == ConvertMeshesToPolysurfacesSetting.SETTING_ID)?.Value as bool?;
    return value is true;
  }
}
