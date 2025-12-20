using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dConversionSettingsFactory : IConversionSettingsFactory<Plant3dConversionSettings>
{
  public Plant3dConversionSettings Create(Document document, string speckleUnits)
  {
    return new Plant3dConversionSettings(document, speckleUnits);
  }
}

