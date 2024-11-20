using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(nameof(DB.Element), 0)]
public class ElementTopLevelConverterToSpeckle : IToSpeckleTopLevelConverter
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly ClassPropertiesExtractor _classPropertiesExtractor;
  private readonly ITypedConverter<DB.Location, Base> _locationConverter;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;

  public ElementTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    ClassPropertiesExtractor classPropertiesExtractor,
    ITypedConverter<DB.Location, Base> locationConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _classPropertiesExtractor = classPropertiesExtractor;
    _locationConverter = locationConverter;
    _converterSettings = converterSettings;
  }

  public Base Convert(object target) => Convert((DB.Element)target);

  public Base Convert(DB.Element target)
  {
    string family = "none";
    switch (target.Document.GetElement(target.GetTypeId()))
    {
      case DB.FamilySymbol symbol:
        family = symbol.FamilyName;
        break;
      case DB.ElementType type:
        family = type.FamilyName;
        break;
    }

    string category = target.Category?.Name ?? "none";

    Base revitObject =
      new()
      {
        ["name"] = target.Name,
        ["category"] = category,
        ["family"] = family,
        ["units"] = _converterSettings.Current.SpeckleUnits
      };

    // get location if any
    if (target.Location is DB.Location location) // location can be null
    {
      try
      {
        revitObject["location"] = _locationConverter.Convert(location);
      }
      catch (ValidationException)
      {
        // location was not a supported, do not attach to base element
      }
    }

    // get the display value
    List<Objects.Geometry.Mesh> displayValue = GetDisplayValue(target);
    if (displayValue.Count > 0)
    {
      revitObject["displayValue"] = displayValue;
    }

    // add any additional class properties
    Dictionary<string, object?>? classProperties = _classPropertiesExtractor.GetClassProperties(target);
    if (classProperties is not null)
    {
      foreach (string key in classProperties.Keys)
      {
        revitObject[$"{key}"] = classProperties[key];
      }
    }

    // determine if this element has any children elements that need to be converted.
    // this is a bespoke method by class type.
    var children = GetElementChildren(target).ToList();
    if (children.Count > 0)
    {
      revitObject.SetDetachedProp("elements", children);
    }

    return revitObject;
  }

  // Custom handling of display values for some elements
  private List<Objects.Geometry.Mesh> GetDisplayValue(DB.Element element)
  {
    switch (element)
    {
      // curtain and stacked walls should have their display values in their children
      case DB.Wall wall:
        return wall.CurtainGrid is not null || wall.IsStackedWall
          ? new()
          : _displayValueExtractor.GetDisplayValue(element);

      case DBA.Railing railing:
        var railingDisplay = _displayValueExtractor.GetDisplayValue(railing);
        if (railing.TopRail != DB.ElementId.InvalidElementId)
        {
          var topRail = _converterSettings.Current.Document.GetElement(railing.TopRail);
          railingDisplay.AddRange(_displayValueExtractor.GetDisplayValue(topRail));
        }
        return railingDisplay;

      // POC: footprint roofs can have curtain walls in them. Need to check if they can also have non-curtain wall parts, bc currently not skipping anything.
      // case DB.FootPrintRoof footPrintRoof:

      default:
        return _displayValueExtractor.GetDisplayValue(element);
    }
  }

  private IEnumerable<Base> GetElementChildren(DB.Element element)
  {
    switch (element)
    {
      case DB.Wall wall:
        var wallChildren = GetWallChildren(wall);
        foreach (var child in wallChildren)
        {
          yield return child;
        }
        break;

      case DB.FootPrintRoof footPrintRoof:
        var footPrintRoofChildren = GetFootPrintRoofChildren(footPrintRoof);
        foreach (var child in footPrintRoofChildren)
        {
          yield return child;
        }
        break;
    }
  }

  private IEnumerable<Base> GetWallChildren(DB.Wall wall)
  {
    List<DB.ElementId> wallChildrenIds = new();
    if (wall.CurtainGrid is DB.CurtainGrid grid)
    {
      wallChildrenIds.AddRange(grid.GetMullionIds());
      wallChildrenIds.AddRange(grid.GetPanelIds());
    }
    else if (wall.IsStackedWall)
    {
      wallChildrenIds.AddRange(wall.GetStackedWallMemberIds());
    }

    foreach (var childId in wallChildrenIds)
    {
      yield return Convert(_converterSettings.Current.Document.GetElement(childId));
    }
  }

  // Shockingly, roofs can have curtain grids on them. I guess it makes sense: https://en.wikipedia.org/wiki/Louvre_Pyramid
  private IEnumerable<Base> GetFootPrintRoofChildren(DB.FootPrintRoof footPrintRoof)
  {
    List<DB.ElementId> footPrintRoofChildrenIds = new();
    if (footPrintRoof.CurtainGrids is { } gs)
    {
      foreach (DB.CurtainGrid grid in gs)
      {
        footPrintRoofChildrenIds.AddRange(grid.GetMullionIds());
        footPrintRoofChildrenIds.AddRange(grid.GetPanelIds());
      }
    }

    foreach (var childId in footPrintRoofChildrenIds)
    {
      yield return Convert(_converterSettings.Current.Document.GetElement(childId));
    }
  }
}
