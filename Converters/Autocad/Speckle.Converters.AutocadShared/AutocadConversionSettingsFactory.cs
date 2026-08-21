using Speckle.Converters.Common;
using Speckle.InterfaceGenerator;

namespace Speckle.Converters.Autocad;

[GenerateAutoInterface]
public class AutocadConversionSettingsFactory(IHostToSpeckleUnitConverter<ADB.UnitsValue> unitsConverter)
  : IAutocadConversionSettingsFactory
{
  public AutocadConversionSettings Create(
    Document document,
    bool applyTransform = false,
    AutocadModelPlacement placement = AutocadModelPlacement.DrawingWcs
  )
  {
    AG.Matrix3d? currentUcs =
      document.Editor.CurrentUserCoordinateSystem == AG.Matrix3d.Identity
        ? null
        : document.Editor.CurrentUserCoordinateSystem;

    var options = new Dictionary<string, AG.Matrix3d>(StringComparer.Ordinal)
    {
      ["drawingWcs"] = AG.Matrix3d.Identity,
      ["currentUcs"] = currentUcs?.Inverse() ?? AG.Matrix3d.Identity,
    };
    string source = placement == AutocadModelPlacement.CurrentUcs ? "currentUcs" : "drawingWcs";
    AG.Matrix3d? sourceToWcs = source == "currentUcs" ? currentUcs : null;

    return new(
      document,
      applyTransform ? sourceToWcs : null,
      unitsConverter.ConvertOrThrow(document.Database.Insunits),
      applyTransform,
      source,
      options
    );
  }
}
