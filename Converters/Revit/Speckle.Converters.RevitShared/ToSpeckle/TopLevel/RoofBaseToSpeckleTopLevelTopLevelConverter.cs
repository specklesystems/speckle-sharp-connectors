using Autodesk.Revit.DB;
using Objects.BuiltElements.Revit.RevitRoof;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.RoofBase), 0)]
internal sealed class RoofBaseToSpeckleTopLevelTopLevelConverter
  : BaseTopLevelConverterToSpeckle<DB.RoofBase, RevitRoof>
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ParameterObjectAssigner _parameterObjectAssigner;

  public RoofBaseToSpeckleTopLevelTopLevelConverter(
    DisplayValueExtractor displayValueExtractor,
    ParameterObjectAssigner parameterObjectAssigner
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _parameterObjectAssigner = parameterObjectAssigner;
  }

  public override RevitRoof Convert(RoofBase target)
  {
    RevitRoof revitRoof = new();
    var elementType = (ElementType)target.Document.GetElement(target.GetTypeId());
    revitRoof.type = elementType.Name;
    revitRoof.family = elementType.FamilyName;

    _parameterObjectAssigner.AssignParametersToBase(target, revitRoof);
    revitRoof.displayValue = _displayValueExtractor.GetDisplayValue(target);
    // POC: removing hosted elements from parents
    // revitRoof.elements = _hostedElementConverter.ConvertHostedElements(target.GetHostedElementIds()).ToList();

    return revitRoof;
  }
}
