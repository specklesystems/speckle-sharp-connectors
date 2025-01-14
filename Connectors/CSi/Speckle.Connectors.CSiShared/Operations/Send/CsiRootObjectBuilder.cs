using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Converters.CSiShared.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.CSiShared.Builders;

/// <summary>
/// Manages the conversion of CSi model objects and creation of proxy relationships.
/// </summary>
/// <remarks>
/// Key responsibilities:
/// - Converts ICsiWrappers to DataObjects (ETABS/SAP objects)
/// - Manages material and section proxy creation
/// - Establishes relationships between objects, sections, and materials
///
/// Design principles:
/// - Two-stage process: conversion then relationship establishment
/// - Objects grouped by type for efficient relationship processing
/// - Proxies created through dedicated unpackers
/// - Relationships managed through separate relationship manager
/// - Error handling at each stage preserves partial success
/// </remarks>
public class CsiRootObjectBuilder : IRootObjectBuilder<ICsiWrapper>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettings;
  private readonly CsiSendCollectionManager _sendCollectionManager;
  private readonly ISectionUnpacker _sectionUnpacker;
  private readonly IMaterialUnpacker _materialUnpacker;
  private readonly IProxyRelationshipManager _proxyRelationshipManager;
  private readonly Dictionary<string, List<Base>> _convertedObjectsForProxies = [];
  private readonly ILogger<CsiRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ICsiApplicationService _csiApplicationService;

  public CsiRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<CsiConversionSettings> converterSettings,
    CsiSendCollectionManager sendCollectionManager,
    ISectionUnpacker sectionUnpacker,
    IMaterialUnpacker materialUnpacker,
    IProxyRelationshipManager proxyRelationshipManager,
    ILogger<CsiRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ICsiApplicationService csiApplicationService
  )
  {
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
    _sectionUnpacker = sectionUnpacker;
    _materialUnpacker = materialUnpacker;
    _proxyRelationshipManager = proxyRelationshipManager;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _csiApplicationService = csiApplicationService;
  }

  /// <summary>
  /// Converts CSi objects and establishes proxy relationships.
  /// </summary>
  /// <remarks>
  /// Process flow:
  /// 1. Converts each ICsiWrapper to appropriate DataObject
  /// 2. Groups frame/shell objects for proxy relationships
  /// 3. Creates material and section proxies
  /// 4. Establishes relationships between all components
  ///
  /// Error handling ensures partial success is preserved even if some
  /// objects fail conversion or relationship establishment.
  /// </remarks>
  public async Task<RootObjectBuilderResult> BuildAsync(
    IReadOnlyList<ICsiWrapper> csiObjects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");

    string modelFileName = _csiApplicationService.SapModel.GetModelFilename(false) ?? "Unnamed model";
    Collection rootObjectCollection =
      new() { name = modelFileName, ["units"] = _converterSettings.Current.SpeckleUnits };

    List<SendConversionResult> results = new(csiObjects.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (ICsiWrapper csiObject in csiObjects)
      {
        cancellationToken.ThrowIfCancellationRequested();
        using var _2 = _activityFactory.Start("Convert");

        var result = ConvertCsiObject(csiObject, rootObjectCollection, sendInfo.ProjectId);
        results.Add(result);

        count++;
        onOperationProgressed.Report(new("Converting", (double)count / csiObjects.Count));
        await Task.Yield();
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    using (var _ = _activityFactory.Start("Process Proxies"))
    {
      ProcessSectionsAndMaterials(rootObjectCollection);
    }

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  /// <summary>
  /// Converts a single ICsiWrapper to a DataObject.
  /// </summary>
  /// <remarks>
  /// - Checks cache before conversion
  /// - Only successful conversions added to collection
  /// - Frame and shell objects tracked for proxy relationships
  /// - Uses application-specific collection management
  /// </remarks>
  private SendConversionResult ConvertCsiObject(ICsiWrapper csiObject, Collection typeCollection, string projectId)
  {
    string applicationId = $"{csiObject.ObjectType}{csiObject.Name}"; // TODO: NO! Use GUID
    string sourceType = csiObject.ObjectName;

    try
    {
      Base converted;
      if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
      {
        converted = value;
      }
      else
      {
        converted = _rootToSpeckleConverter.Convert(csiObject);
      }

      var collection = _sendCollectionManager.AddObjectCollectionToRoot(converted, typeCollection);
      collection.elements.Add(converted); // On successful conversion

      if (sourceType != ModelObjectType.FRAME.ToString() && sourceType != ModelObjectType.SHELL.ToString())
      {
        return new(Status.SUCCESS, applicationId, sourceType, converted);
      }

      if (!_convertedObjectsForProxies.TryGetValue(sourceType, out List<Base>? typeCollectionForProxies))
      {
        typeCollectionForProxies = ([]);
        _convertedObjectsForProxies[sourceType] = typeCollectionForProxies;
      }

      typeCollectionForProxies.Add(converted);

      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex); // On failed conversion
    }
  }

  /// <summary>
  /// Creates and links material and section proxies.
  /// </summary>
  /// <remarks>
  /// Order of operations is important:
  /// 1. Create material proxies (no dependencies)
  /// 2. Create section proxies (references materials)
  /// 3. Establish relationships (needs both proxies and converted objects)
  /// </remarks>
  private void ProcessSectionsAndMaterials(Collection rootObjectCollection)
  {
    try
    {
      using var activity = _activityFactory.Start("Process Materials and Sections");

      var materialProxies = _materialUnpacker.UnpackMaterials(rootObjectCollection);
      var sectionProxies = _sectionUnpacker.UnpackSections(rootObjectCollection);

      _proxyRelationshipManager.EstablishRelationships(_convertedObjectsForProxies, materialProxies, sectionProxies);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to process section and material proxies");
    }
  }
}
