using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.RevitShared.Services;

// POC: feels like this is a context thing, and we should be calculating this occasionally?
// needs some thought as to how it could be done, could leave as is for now
[GenerateAutoInterface]
public sealed class ScalingServiceToSpeckle : IScalingServiceToSpeckle
{
  private readonly double _defaultLengthConversionFactor;

  // POC: this seems like the reverse relationship
  public ScalingServiceToSpeckle(RevitContext revitContext)
  {
    // Always use the main document's scaling factor to ensure consistency for both main model and linked model elements
    // this need became apparent for CNX-1431 fix
    _defaultLengthConversionFactor = revitContext.GetMainDocumentScalingFactor();
  }

  // POC: throughout Revit conversions there's lots of comparison to check the units are valid
  // atm we assume that the scaling is fixed for the duration of a conversion and completely dependent on the main
  // model settings (not linked models), hence the explicit GetMainDocumentScalingFactor in RevitContext
  public double ScaleLength(double length) => length * _defaultLengthConversionFactor;

  // POC: not sure about this???
  public double Scale(double value, ForgeTypeId forgeTypeId) => ScaleStatic(value, forgeTypeId);

  // POC: not sure why this is needed???
  private static double ScaleStatic(double value, ForgeTypeId forgeTypeId) =>
    UnitUtils.ConvertFromInternalUnits(value, forgeTypeId);
}
