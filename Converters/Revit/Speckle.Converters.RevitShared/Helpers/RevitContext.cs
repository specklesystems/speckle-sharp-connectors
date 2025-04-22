using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.Helpers;

public class RevitContext
{
  private UIApplication? _uiApplication;

  public UIApplication? UIApplication
  {
    get => _uiApplication;
    set
    {
      if (_uiApplication != null)
      {
        throw new ArgumentException("UIApplication already set");
      }

      _uiApplication = value;
    }
  }

  /// <summary>
  /// Gets the scaling factor for the main document.
  /// This should be used for all scaling operations to ensure consistency
  /// between main model and linked model elements.
  /// </summary>
  /// <returns>The scaling factor for converting from internal units to the main document's units</returns>
  public double GetMainDocumentScalingFactor()
  {
    var mainModelDoc =
      UIApplication.NotNull().ActiveUIDocument?.Document
      ?? throw new SpeckleException("Unable to retrieve active UI document");

    DB.Units documentUnits = mainModelDoc.GetUnits();
    FormatOptions formatOptions = documentUnits.GetFormatOptions(SpecTypeId.Length);
    var lengthUnitsTypeId = formatOptions.GetUnitTypeId();

    return UnitUtils.ConvertFromInternalUnits(1, lengthUnitsTypeId);
  }
}
