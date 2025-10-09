using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.Common.ToSpeckle;
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
  private readonly LinkedModelElementCacheScoped _linkedModelElementCacheScoped;

  public ElementTopLevelConverterToSpeckle(
    DisplayValueExtractor displayValueExtractor,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    LinkedModelElementCacheScoped linkedModelElementCacheScoped,
    PropertiesExtractor propertiesExtractor,
    LevelExtractor levelExtractor,
    ITypedConverter<DB.Location, Base> locationConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings
  )
  {
    _displayValueExtractor = displayValueExtractor;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _linkedModelElementCacheScoped = linkedModelElementCacheScoped;
    _propertiesExtractor = propertiesExtractor;
    _levelExtractor = levelExtractor;
    _locationConverter = locationConverter;
    _converterSettings = converterSettings;
  }

  public Base Convert(object target) => Convert((DB.Element)target);

  private RevitObject Convert(DB.Element target)
  {
    // for linked model elements, check the cache. an "early exit" with cached properties saves expensive re-extraction
    // this happens when we have multiple instances of the same linked model.
    if (target.Document.IsLinked)
    {
      if (
        _linkedModelElementCacheScoped.TryGetCachedElement(
          target.Document.PathName,
          target.UniqueId,
          out Base? cachedElement
        )
      )
      {
        var cachedRevitObject = (RevitObject)cachedElement;

        // Re-extract display values (different per instance due to transforms)
        // but reuse everything else (properties, location, level, etc.)
        List<DisplayValueResult> freshDisplayValues = _displayValueExtractor.GetDisplayValue(target);
        List<Base> freshProxifiedDisplayValues = ProcessDisplayValues(freshDisplayValues);

        // Create new RevitObject with cached properties but fresh display values
        return new RevitObject
        {
          name = cachedRevitObject.name,
          type = cachedRevitObject.type,
          family = cachedRevitObject.family,
          level = cachedRevitObject.level,
          category = cachedRevitObject.category,
          location = cachedRevitObject.location,
          elements = cachedRevitObject.elements,
          displayValue = freshProxifiedDisplayValues, // ← only this is fresh
          properties = cachedRevitObject.properties,
          units = cachedRevitObject.units
        };
      }
    }

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
    List<DisplayValueResult> displayValuesWithTransforms = _displayValueExtractor.GetDisplayValue(target);

    // process display values and create instance proxies where applicable
    List<Base> proxifiedDisplayValues = ProcessDisplayValues(displayValuesWithTransforms);

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

    // store in cache if linked model element
    if (target.Document.IsLinked)
    {
      _linkedModelElementCacheScoped.StoreCachedElement(target.Document.PathName, target.UniqueId, revitObject);
    }

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

  /// <summary>
  /// Processes display values with transforms and creates instance proxies for meshes that can be instanced.
  /// </summary>
  /// <returns>List of processed display values, with meshes replaced by instance proxies where applicable</returns>
  private List<Base> ProcessDisplayValues(List<DisplayValueResult> displayValues)
  {
    List<Base> proxifiedDisplayValues = new();

    foreach (var displayValue in displayValues)
    {
      // check if this is a mesh with a transform - potential instance scenario
      // assumption here is that if we have matrix for corresponding base it is instance-able
      if (displayValue.Geometry is SOG.Mesh mesh && displayValue.Transform is not null)
      {
        var instanceProxy = CreateOrGetInstanceProxy(mesh, displayValue.Transform.Value);
        proxifiedDisplayValues.Add(instanceProxy);
      }
      else
      {
        proxifiedDisplayValues.Add(displayValue.Geometry);
      }
    }

    return proxifiedDisplayValues;
  }

  /// <summary>
  /// Creates or retrieves an instance proxy for a mesh, managing instance definitions and caching.
  /// </summary>
  private InstanceProxy CreateOrGetInstanceProxy(SOG.Mesh mesh, Matrix4x4 transform)
  {
    var instanceDefinitionId = MeshInstanceIdGenerator.GenerateUntransformedMeshId(mesh);

    // ensure instance definition exists
    if (!_revitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap.ContainsKey(instanceDefinitionId))
    {
      var newInstanceDefinition = new InstanceDefinitionProxy
      {
        applicationId = instanceDefinitionId,
        objects = new List<string> { mesh.applicationId.NotNull() },
        maxDepth = 1,
        name = instanceDefinitionId,
      };
      _revitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap.Add(instanceDefinitionId, newInstanceDefinition);
    }

    // cache the untransformed mesh object if not already cached
    if (!_revitToSpeckleCacheSingleton.InstancedObjects.ContainsKey(instanceDefinitionId))
    {
      _revitToSpeckleCacheSingleton.InstancedObjects.Add(instanceDefinitionId, mesh);
    }

    // create and return instance proxy with transform
    var instanceProxy = new InstanceProxy
    {
      applicationId = Guid.NewGuid().ToString(),
      definitionId = instanceDefinitionId,
      transform = transform,
      maxDepth = 1,
      units = mesh.units
    };

    return instanceProxy;
  }
}
