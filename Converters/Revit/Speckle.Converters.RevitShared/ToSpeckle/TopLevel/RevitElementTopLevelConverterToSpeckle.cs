using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Extensions;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Converters.RevitShared.ToSpeckle.Properties;
using Speckle.DoubleNumerics;
using Speckle.Objects.Data;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Converters.RevitShared.ToSpeckle;

[NameAndRankValue(typeof(DB.Element), 0)]
public class ElementTopLevelConverterToSpeckle : IToSpeckleTopLevelConverter
{
  private readonly DisplayValueExtractor _displayValueExtractor;
  private readonly PropertiesExtractor _propertiesExtractor;
  private readonly ITypedConverter<DB.Location, Base> _locationConverter;
  private readonly LevelExtractor _levelExtractor;
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;

  public ElementTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    PropertiesExtractor propertiesExtractor,
    LevelExtractor levelExtractor,
    ITypedConverter<DB.Location, Base> locationConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _propertiesExtractor = propertiesExtractor;
    _levelExtractor = levelExtractor;
    _locationConverter = locationConverter;
    _converterSettings = converterSettings;
  }

  public Base Convert(object target) => Convert((DB.Element)target);

  public RevitObject Convert(DB.Element target)
  {
    string category = target.Category?.Name ?? "none";

    // special case for direct shapes: use builtin category instead
    if (target is DB.DirectShape ds)
    {
      // Clean up built-in name by removing "OST" prefixes
      category = ds
        .Category.GetBuiltInCategory()
        .ToString()
        .Replace("OST_IOS", "") //for OST_IOSModelGroups
        .Replace("OST_MEP", "") //for OST_MEPSpaces
        .Replace("OST_", "") //for any other OST_blablabla
        .Replace("_", " ");
    }

    string name = $"{category} - {target.Name}"; // Note: I find this looks better in the frontend.
    string familyName = "none";
    string typeName = "none";
    switch (target.Document.GetElement(target.GetTypeId()))
    {
      case DB.FamilySymbol symbol:
        familyName = symbol.FamilyName;
        typeName = symbol.Name;
        break;
      case DB.ElementType type:
        familyName = type.FamilyName;
        typeName = type.Name;
        break;
    }

    // get location if any
    Base? convertedLocation = null;
    switch (target)
    {
      // skip these objects, if location is redundant
      case DB.ModelCurve:
        break;

      default:
        if (target.Location is DB.Location location and (DB.LocationCurve or DB.LocationPoint)) // location can be null
        {
          try
          {
            convertedLocation = _locationConverter.Convert(location);
          }
          catch (ValidationException)
          {
            // NOTE: i've improved the if check above to make sure we never reach here
            // we were throwing a lot here for various elements (e.g. floors) and we would
            // be slowing things down
            // location was not a supported, do not attach to base element
          }
        }
        break;
    }

    // get the display value
    List<(Base, Matrix4x4?)> displayValuesWithTransforms = _displayValueExtractor.GetDisplayValue(target);
    var displayValues = displayValuesWithTransforms.ConvertAll(displayValueConverter => displayValueConverter.Item1);

    List<Base> proxifiedDisplayValues = new();
    foreach ((Base, Matrix4x4?) displayValueWithTransform in displayValuesWithTransforms)
    {
      if (displayValueWithTransform.Item1 is SOG.Mesh && displayValueWithTransform.Item2 is not null)
      {
        // potential instances scenario here
        var unbakedMesh = displayValueWithTransform.Item1 as SOG.Mesh;
        if (unbakedMesh is not null)
        {
          var instanceDefinitionId = Math.Abs(unbakedMesh.vertices[0]).ToString();
          if (
            _revitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap.TryGetValue(
              instanceDefinitionId,
              out InstanceDefinitionProxy? instanceDefinition
            )
          )
          {
            // instanceDefinition.objects.Add(unbakedMesh.applicationId.NotNull());
          }
          else
          {
            var newInstanceDefinition = new InstanceDefinitionProxy()
            {
              applicationId = instanceDefinitionId,
              objects = new List<string> { unbakedMesh.applicationId.NotNull() },
              maxDepth = 1,
              name = instanceDefinitionId,
            };
            _revitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap.Add(instanceDefinitionId, newInstanceDefinition);
          }

          _revitToSpeckleCacheSingleton.InstancedObjects.Add(instanceDefinitionId, unbakedMesh);
          var instanceProxy = new InstanceProxy()
          {
            applicationId = unbakedMesh.applicationId.NotNull(),
            definitionId = instanceDefinitionId,
            transform = displayValueWithTransform.Item2.Value,
            maxDepth = 1,
            units = unbakedMesh.units
          };
          proxifiedDisplayValues.Add(instanceProxy);
        }
        else
        {
          proxifiedDisplayValues.Add(displayValueWithTransform.Item1);
        }
      }
      else
      {
        proxifiedDisplayValues.Add(displayValueWithTransform.Item1);
      }
    }

    // assumption here is that if we have matrix for corresponding base it is instancable

    // get level
    string? level = _levelExtractor.GetLevelName(target);

    // get children elements
    // this is a bespoke method by class type.
    var children = GetElementChildren(target).ToList();

    // get properties
    Dictionary<string, object?> properties = _propertiesExtractor.GetProperties(target);

    RevitObject revitObject =
      new()
      {
        name = name,
        type = typeName,
        family = familyName,
        level = level,
        category = category,
        location = convertedLocation,
        elements = children,
        displayValue = proxifiedDisplayValues,
        properties = properties,
        units = _converterSettings.Current.SpeckleUnits
      };

    return revitObject;
  }

  private IEnumerable<RevitObject> GetElementChildren(DB.Element element)
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

  private IEnumerable<RevitObject> GetWallChildren(DB.Wall wall)
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
  private IEnumerable<RevitObject> GetFootPrintRoofChildren(DB.FootPrintRoof footPrintRoof)
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
