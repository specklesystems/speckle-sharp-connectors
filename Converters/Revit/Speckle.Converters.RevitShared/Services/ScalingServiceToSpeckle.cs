using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;

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
    // As part of CNX-1431, we see that scaling must always be w.r.t. the main model
    // for linked models, Revit already handles the scaling (if any) between main and linked models internally
    // since we cache main model elements and not linked model elements (linked model elements always require reconversion),
    // consecutive sends would see the second send being initialized with the settings of the linked model which is wrong
    // hence why the below enforces using the main model doc
    var mainModelDoc =
      revitContext.UIApplication.NotNull().ActiveUIDocument
      ?? throw new SpeckleException("Unable to retrieve active UI document");
    DB.Units documentUnits = mainModelDoc.Document.GetUnits();
    FormatOptions formatOptions = documentUnits.GetFormatOptions(SpecTypeId.Length);
    var lengthUnitsTypeId = formatOptions.GetUnitTypeId();
    _defaultLengthConversionFactor = ScaleStatic(1, lengthUnitsTypeId);
  }

  // POC: throughout Revit conversions there's lots of comparison to check the units are valid
  // atm we seem to be expecting that this is correct and that the scaling will be fixed for the duration
  // of a conversion, but...  I have some concerns that the units and the conversion may change
  // this needs to be considered and perhaps scaling should be part of the context, or at least part of the IRevitConversionContextStack
  // see comments on above ScalingServiceToSpeckle as to why this is not an issue for linked models - but maybe other gremlins exist?
  public double ScaleLength(double length) => length * _defaultLengthConversionFactor;

  // POC: not sure about this???
  public double Scale(double value, ForgeTypeId forgeTypeId) => ScaleStatic(value, forgeTypeId);

  // POC: not sure why this is needed???
  private static double ScaleStatic(double value, ForgeTypeId forgeTypeId) =>
    UnitUtils.ConvertFromInternalUnits(value, forgeTypeId);
}
