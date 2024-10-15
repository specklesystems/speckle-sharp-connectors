using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects;
using Speckle.Objects.BuiltElements.Revit;
using Speckle.Objects.BuiltElements.Revit.RevitRoof;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.FootPrintRoof), 0)]
public class FootPrintRoofToSpeckleTopLevelConverter
  : BaseTopLevelConverterToSpeckle<DB.FootPrintRoof, RevitFootprintRoof>
{
  private readonly ITypedConverter<DB.Level, SOBR.RevitLevel> _levelConverter;
  private readonly ITypedConverter<DB.ModelCurveArrArray, SOG.Polycurve[]> _modelCurveArrArrayConverter;
  private readonly ParameterValueExtractor _parameterValueExtractor;
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly IRootToSpeckleConverter _converter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public FootPrintRoofToSpeckleTopLevelConverter(
    ITypedConverter<Level, RevitLevel> levelConverter,
    ITypedConverter<ModelCurveArrArray, Polycurve[]> modelCurveArrArrayConverter,
    ParameterValueExtractor parameterValueExtractor,
    DisplayValueExtractor displayValueExtractor,
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _levelConverter = levelConverter;
    _modelCurveArrArrayConverter = modelCurveArrArrayConverter;
    _parameterValueExtractor = parameterValueExtractor;
    _displayValueExtractor = displayValueExtractor;
    _converter = converter;
    _converterSettings = converterSettings;
  }

  public override RevitFootprintRoof Convert(FootPrintRoof target)
  {
    var baseLevel = _parameterValueExtractor.GetValueAsDocumentObject<DB.Level>(
      target,
      DB.BuiltInParameter.ROOF_BASE_LEVEL_PARAM
    );

    // We don't currently validate the success of this TryGet, it is assumed some Roofs don't have a top-level.
    _parameterValueExtractor.TryGetValueAsDocumentObject<DB.Level>(
      target,
      DB.BuiltInParameter.ROOF_UPTO_LEVEL_PARAM,
      out var topLevel
    );

    var elementType = (ElementType)target.Document.GetElement(target.GetTypeId());
    List<Speckle.Objects.Geometry.Mesh> displayValue = _displayValueExtractor.GetDisplayValue(target);

    RevitFootprintRoof speckleFootprintRoof =
      new()
      {
        type = elementType.Name,
        family = elementType.FamilyName,
        level = _levelConverter.Convert(baseLevel),
        cutOffLevel = topLevel is not null ? _levelConverter.Convert(topLevel) : null,
        displayValue = displayValue,
        units = _converterSettings.Current.SpeckleUnits
      };

    // Shockingly, roofs can have curtain grids on them. I guess it makes sense: https://en.wikipedia.org/wiki/Louvre_Pyramid
    if (target.CurtainGrids is { } gs)
    {
      List<Base> roofChildren = new();
      foreach (CurtainGrid grid in gs)
      {
        roofChildren.AddRange(ConvertElements(grid.GetMullionIds()));
        roofChildren.AddRange(ConvertElements(grid.GetPanelIds()));
      }

      if (speckleFootprintRoof.GetDetachedProp("elements") is List<Base> elements)
      {
        elements.AddRange(roofChildren);
      }
      else
      {
        speckleFootprintRoof.SetDetachedProp("elements", roofChildren);
      }
    }

    // POC: CNX-9396 again with the incorrect assumption that the first profile is the floor and subsequent profiles
    // are voids
    // POC: CNX-9403 in current connector, we are doing serious gymnastics to get the slope of the floor as defined by
    // slope arrow. The way we are doing it relies on dynamic props and only works for Revit <-> Revit
    var profiles = _modelCurveArrArrayConverter.Convert(target.GetProfiles());
    speckleFootprintRoof.outline = profiles.FirstOrDefault().NotNull();
    speckleFootprintRoof.voids = profiles.Skip(1).ToList<ICurve>();

    return speckleFootprintRoof;
  }

  private IEnumerable<Base> ConvertElements(IEnumerable<DB.ElementId> elementIds)
  {
    foreach (DB.ElementId elementId in elementIds)
    {
      yield return _converter.Convert(_converterSettings.Current.Document.GetElement(elementId));
    }
  }
}
