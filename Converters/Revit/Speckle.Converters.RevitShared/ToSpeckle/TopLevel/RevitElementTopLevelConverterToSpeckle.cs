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

  private RevitObject Convert(DB.Element target)
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
    List<DisplayValueResult> displayValuesWithTransforms = _displayValueExtractor.GetDisplayValue(target);

    // process display values and create instance proxies where applicable
    List<Base> proxifiedDisplayValues = ProcessDisplayValues(target.Id.ToString(), displayValuesWithTransforms);

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
    var childrenIds = element.GetKnownChildrenElements();
    foreach (var childrenId in childrenIds)
    {
      var childElement = _converterSettings.Current.Document.GetElement(childrenId);
      yield return Convert(childElement);
    }
  }

  /// <summary>
  /// Processes display values with transforms and creates instance proxies for meshes that can be instanced.
  /// Also populates material proxy objects lists with the appropriate mesh IDs based on whether geometry is instanced or not.
  /// </summary>
  /// <returns>
  /// List of processed display values, with meshes replaced by instance proxies where applicable.
  /// Non-instance geometry is returned as-is.
  /// </returns>
  /// <remarks>
  /// <para>
  /// This is a bit of a code smell. This method is doing too much, "this ... AND this...".
  /// </para>
  /// <para>
  /// But, given a mesh:
  /// - if it has a transform, mesh is converted to instance proxy, and the definition mesh ID is added to material proxies
  /// - if it doesn't have a transform, it remains as a regular mesh, and its own ID is added to material proxies
  /// - other geometry types pass through unchanged
  /// </para>
  /// <para>
  /// This is where material proxy population occurs (deferred from <see cref="MeshByMaterialDictionaryToSpeckle.Convert"/>)
  /// to ensure we use definition mesh IDs for instances rather than individual instance mesh IDs.
  /// </para>
  /// </remarks>
  private List<Base> ProcessDisplayValues(string elementId, List<DisplayValueResult> displayValues)
  {
    List<Base> proxifiedDisplayValues = new();

    foreach (var displayValue in displayValues)
    {
      // check if this is a mesh with a transform - potential instance scenario
      if (displayValue.Geometry is SOG.Mesh mesh && displayValue.Transform is not null)
      {
        var instanceProxy = CreateOrGetInstanceProxy(elementId, mesh, displayValue.Transform.Value);
        proxifiedDisplayValues.Add(instanceProxy);

        // add the definition mesh ID to material proxy, not the instance mesh
        // method technically is a "Try" but logs internally, so we don't have a return to check
        _revitToSpeckleCacheSingleton.AddMeshToMaterialProxy(elementId, mesh, isInstance: true);
      }
      else if (displayValue.Geometry is SOG.Mesh nonInstanceMesh)
      {
        // non-instance mesh - add its own ID to material proxy
        // method technically is a "Try" but logs internally, so we don't have a return to check
        _revitToSpeckleCacheSingleton.AddMeshToMaterialProxy(elementId, nonInstanceMesh, isInstance: false);
        proxifiedDisplayValues.Add(nonInstanceMesh);
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
  /// <remarks>
  /// <para>
  /// This method generates a deterministic instance definition ID based on the untransformed mesh geometry using
  /// <see cref="MeshInstanceIdGenerator.GenerateUntransformedMeshId"/>. Multiple instances with identical geometry
  /// will share the same definition.
  /// </para>
  /// <para>
  /// The method manages two caches:
  /// - <see cref="RevitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap"/>: Tracks instance definitions and which elements use them
  /// - <see cref="RevitToSpeckleCacheSingleton.InstancedObjects"/>: Stores the actual definition meshes for later serialization
  /// </para>
  /// </remarks>
  private InstanceProxy CreateOrGetInstanceProxy(string elementId, SOG.Mesh mesh, Matrix4x4 transform)
  {
    var instanceDefinitionId = MeshInstanceIdGenerator.GenerateUntransformedMeshId(mesh);
    var materialId = _revitToSpeckleCacheSingleton.GetMaterialId(elementId, mesh);
    instanceDefinitionId += materialId;

    // We need to attach element id relationship to proxy singleton for send caching.
    // Send caching skips whole DB.Element that turn into RevitDataObject. since we have instance proxies in RevitDataObject but
    // its definitions outside of caching mechanism, this elementId helps us to filter which definition proxies should be attached to the root
    if (
      _revitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap.TryGetValue(
        instanceDefinitionId,
        out var instanceDefinition
      )
    )
    {
      instanceDefinition.elementIds.Add(elementId);
    }
    else
    {
      var newInstanceDefinition = new InstanceDefinitionProxy
      {
        applicationId = instanceDefinitionId,
        objects = new List<string> { mesh.applicationId.NotNull() },
        maxDepth = 0,
        name = instanceDefinitionId,
      };
      _revitToSpeckleCacheSingleton.InstanceDefinitionProxiesMap.Add(
        instanceDefinitionId,
        ([elementId], newInstanceDefinition)
      );
    }

    // some comment valid here as above if statement, since we store original meshes outside of RevitDataObject, we need to know which of them will be attached.
    if (_revitToSpeckleCacheSingleton.InstancedObjects.TryGetValue(instanceDefinitionId, out var instancedObject))
    {
      instancedObject.elementIds.Add(elementId);
    }
    else
    {
      _revitToSpeckleCacheSingleton.InstancedObjects.Add(instanceDefinitionId, ([elementId], mesh));
    }

    // create and return instance proxy with transform
    var instanceProxy = new InstanceProxy
    {
      applicationId = Guid.NewGuid().ToString(),
      definitionId = instanceDefinitionId,
      transform = transform,
      maxDepth = 0,
      units = mesh.units
    };

    return instanceProxy;
  }
}
