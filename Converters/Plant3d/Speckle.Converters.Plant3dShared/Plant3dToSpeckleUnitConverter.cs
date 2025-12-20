using Speckle.Converters.Common;
using Speckle.Converters.Common.Registration;

namespace Speckle.Converters.Plant3dShared;

public class Plant3dToSpeckleUnitConverter : IApplicationUnitConverter<ADB.UnitsValue>
{
  public double ConvertToSpeckle(ADB.UnitsValue sourceUnit, string targetUnits)
  {
    return sourceUnit.Value;
  }
}

