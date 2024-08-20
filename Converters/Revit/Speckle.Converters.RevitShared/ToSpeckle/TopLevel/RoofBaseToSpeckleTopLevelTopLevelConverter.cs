using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.BuiltElements.Revit.RevitRoof;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.RoofBase), 0)]
internal sealed class RoofBaseToSpeckleTopLevelTopLevelConverter
  : BaseTopLevelConverterToSpeckle<DB.RoofBase, RevitRoof>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;
  private readonly IRevitConversionContextStack _contextStack;

  public RoofBaseToSpeckleTopLevelTopLevelConverter(
    DisplayValueExtractor displayValueExtractor,
    ParameterObjectAssigner parameterObjectAssigner,
    IRevitConversionContextStack contextStack
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _parameterObjectAssigner = parameterObjectAssigner;
    _contextStack = contextStack;
  }

  public override RevitRoof Convert(RoofBase target)
  {
    var elementType = (ElementType)target.Document.GetElement(target.GetTypeId());
    List<Speckle.Objects.Geometry.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitRoof revitRoof =
      new()
      {
        type = elementType.Name,
        family = elementType.FamilyName,
        displayValue = displayValue,
        units = _contextStack.Current.SpeckleUnits
      };

    _parameterObjectAssigner.AssignParametersToBase(target, revitRoof);
    return revitRoof;
  }
}
