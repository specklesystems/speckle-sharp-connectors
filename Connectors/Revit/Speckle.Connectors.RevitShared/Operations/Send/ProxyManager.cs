using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.Operations.Send;

/// <summary>
/// Manages the creation and organization of all proxy objects (instances, materials, levels, etc.).
/// </summary>
public class ProxyManager
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<(Transform transform, string units), Matrix4x4> _transformConverter;
  private readonly ElementUnpacker _elementUnpacker;
  private readonly LevelUnpacker _levelUnpacker;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;
  private readonly ILogger<ProxyManager> _logger;

  public ProxyManager(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    ITypedConverter<(Transform transform, string units), Matrix4x4> transformConverter,
    ElementUnpacker elementUnpacker,
    LevelUnpacker levelUnpacker,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    ILogger<ProxyManager> logger
  )
  {
    _converterSettings = converterSettings;
    _transformConverter = transformConverter;
    _elementUnpacker = elementUnpacker;
    _levelUnpacker = levelUnpacker;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _logger = logger;
  }

  /// <summary>
  /// Adds all types of proxies to the root object based on conversion results
  /// </summary>
  public void AddAllProxies(Collection rootObject, DocumentConversionResults conversionResults)
  {
    // add instance proxies (definitions and instances)
    if (conversionResults.LinkedModelResults?.LinkedModelConversions.Count > 0)
    {
      AddInstanceProxies(rootObject, conversionResults.LinkedModelResults);
    }

    // add material and level proxies
    AddMaterialProxies(rootObject, conversionResults.AllElements);
    AddLevelProxies(rootObject, conversionResults.AllElements);

    // add reference point transform if available
    AddReferencePointTransform(rootObject);
  }

  /// <summary>
  /// Creates and adds instance definition proxies and instance proxies for linked models
  /// </summary>
  private void AddInstanceProxies(Collection rootObject, LinkedModelConversionResults linkedResults)
  {
    var instanceDefinitionProxies = CreateInstanceDefinitionProxies(linkedResults);
    CreateInstanceProxyCollections(rootObject, linkedResults);

    if (instanceDefinitionProxies.Count > 0)
    {
      rootObject[ProxyKeys.INSTANCE_DEFINITION] = instanceDefinitionProxies;
    }
  }

  /// <summary>
  /// Creates instance definition proxies for each unique linked model
  /// </summary>
  private List<InstanceDefinitionProxy> CreateInstanceDefinitionProxies(LinkedModelConversionResults linkedResults)
  {
    var instanceDefinitionProxies = new List<InstanceDefinitionProxy>();

    foreach (var conversionResult in linkedResults.LinkedModelConversions)
    {
      if (conversionResult.ConvertedElementIds.Count == 0)
      {
        _logger.LogWarning(
          "Skipping InstanceDefinitionProxy for '{DocumentPath}' - no elements were converted",
          Path.GetFileName(conversionResult.DocumentPath)
        );
        continue;
      }

      string definitionId = TransformUtils.CreateDefinitionId(conversionResult.DocumentPath);
      string modelName = Path.GetFileNameWithoutExtension(conversionResult.DocumentPath);

      var instanceDefinitionProxy = new InstanceDefinitionProxy
      {
        applicationId = definitionId,
        objects = conversionResult.ConvertedElementIds.ToList(),
        maxDepth = 0, // linked models are at depth 0 for now
        name = modelName
      };

      instanceDefinitionProxies.Add(instanceDefinitionProxy);
    }

    return instanceDefinitionProxies;
  }

  /// <summary>
  /// Creates instance proxy collections and adds them to the appropriate model collections
  /// </summary>
  private void CreateInstanceProxyCollections(Collection rootObject, LinkedModelConversionResults linkedResults)
  {
    foreach (var conversionResult in linkedResults.LinkedModelConversions)
    {
      if (conversionResult.ConvertedElementIds.Count == 0)
      {
        continue;
      }

      string definitionId = TransformUtils.CreateDefinitionId(conversionResult.DocumentPath);
      string modelName = Path.GetFileNameWithoutExtension(conversionResult.DocumentPath);

      var instanceProxies = CreateInstanceProxiesForLinkedModel(conversionResult, definitionId);

      if (instanceProxies.Count > 0)
      {
        AddInstanceProxiesToCollection(rootObject, modelName, instanceProxies);
      }
    }
  }

  /// <summary>
  /// Creates instance proxies for each instance of a linked model
  /// </summary>
  private List<InstanceProxy> CreateInstanceProxiesForLinkedModel(
    LinkedModelConversionResult conversionResult,
    string definitionId
  )
  {
    var instanceProxies = new List<InstanceProxy>();
    int instanceIndex = 0;

    foreach (var instance in conversionResult.Instances)
    {
      instanceIndex++;

      if (instance.Transform == null)
      {
        continue;
      }

      string instanceId = TransformUtils.CreateInstanceId(definitionId, instanceIndex);
      var transformMatrix = _transformConverter.Convert((instance.Transform, _converterSettings.Current.SpeckleUnits));
      var instanceProxy = new InstanceProxy
      {
        applicationId = instanceId,
        definitionId = definitionId,
        transform = transformMatrix,
        units = _converterSettings.Current.SpeckleUnits,
        maxDepth = 0 // linked models are at depth 0 for now
      };

      instanceProxies.Add(instanceProxy);
    }

    return instanceProxies;
  }

  /// <summary>
  /// Adds instance proxies to the appropriate collection structure
  /// </summary>
  private void AddInstanceProxiesToCollection(
    Collection rootObject,
    string modelName,
    List<InstanceProxy> instanceProxies
  )
  {
    // find or create the linked model collection
    var linkedModelCollection = FindOrCreateLinkedModelCollection(rootObject, modelName);

    // find or create the "instances" subcollection
    var instancesCollection = FindOrCreateInstancesCollection(linkedModelCollection);

    // add all instance proxies to the instances collection
    foreach (var instanceProxy in instanceProxies)
    {
      instancesCollection.elements.Add(instanceProxy);
    }
  }

  /// <summary>
  /// Finds or creates a collection for a linked model
  /// </summary>
  private Collection FindOrCreateLinkedModelCollection(Collection rootObject, string modelName)
  {
    // look for existing linked model collection
    foreach (var element in rootObject.elements)
    {
      if (element is Collection collection && collection.name == modelName)
      {
        return collection;
      }
    }

    // create new collection if not found
    var linkedModelCollection = new Collection(modelName);
    rootObject.elements.Add(linkedModelCollection);
    return linkedModelCollection;
  }

  /// <summary>
  /// Finds or creates an "instances" subcollection within a linked model collection
  /// </summary>
  private Collection FindOrCreateInstancesCollection(Collection linkedModelCollection)
  {
    // look for existing instances collection
    foreach (var element in linkedModelCollection.elements)
    {
      if (element is Collection collection && collection.name == "instances")
      {
        return collection;
      }
    }

    // create new instances collection
    var instancesCollection = new Collection("instances");
    linkedModelCollection.elements.Add(instancesCollection);
    return instancesCollection;
  }

  /// <summary>
  /// Adds material proxies to the root object
  /// </summary>
  private void AddMaterialProxies(Collection rootObject, List<Element> allElements)
  {
    var idsAndSubElementIds = _elementUnpacker.GetElementsAndSubelementIdsFromAtomicObjects(allElements);
    var renderMaterialProxies = _revitToSpeckleCacheSingleton.GetRenderMaterialProxyListForObjects(idsAndSubElementIds);

    if (renderMaterialProxies.Count > 0)
    {
      rootObject[ProxyKeys.RENDER_MATERIAL] = renderMaterialProxies;
    }
  }

  /// <summary>
  /// Adds level proxies to the root object
  /// </summary>
  private void AddLevelProxies(Collection rootObject, List<Element> allElements)
  {
    var levelProxies = _levelUnpacker.Unpack(allElements);

    if (levelProxies.Count > 0)
    {
      rootObject[ProxyKeys.LEVEL] = levelProxies;
    }
  }

  /// <summary>
  /// Adds reference point transform information to the root object if available
  /// </summary>
  private void AddReferencePointTransform(Collection rootObject)
  {
    if (_converterSettings.Current.ReferencePointTransform is Transform transform)
    {
      var transformMatrix = ReferencePointHelper.CreateTransformDataForRootObject(transform);
      rootObject[ReferencePointHelper.REFERENCE_POINT_TRANSFORM_KEY] = transformMatrix;
    }
  }
}
