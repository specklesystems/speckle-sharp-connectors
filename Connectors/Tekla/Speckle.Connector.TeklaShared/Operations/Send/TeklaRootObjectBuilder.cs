using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.TeklaShared.Extensions;
using Speckle.Connectors.TeklaShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.TeklaShared;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.TeklaShared.Operations.Send;

public class TeklaRootObjectBuilder : IRootObjectBuilder<TSM.ModelObject>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _converterSettings;
  private readonly SendCollectionManager _sendCollectionManager;
  private readonly ILogger<TeklaRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly TeklaMaterialUnpacker _materialUnpacker;

  public TeklaRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<TeklaConversionSettings> converterSettings,
    SendCollectionManager sendCollectionManager,
    ILogger<TeklaRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    TeklaMaterialUnpacker materialUnpacker
  )
  {
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _materialUnpacker = materialUnpacker;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<TSM.ModelObject> teklaObjects,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");

    var model = new TSM.Model();
    string modelName = model.GetInfo().ModelName ?? "Unnamed model";

    Collection rootObjectCollection = new() { name = modelName };
    rootObjectCollection["units"] = _converterSettings.Current.SpeckleUnits;

    List<SendConversionResult> results = new(teklaObjects.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (TSM.ModelObject teklaObject in teklaObjects)
      {
        cancellationToken.ThrowIfCancellationRequested();
        var result = ConvertTeklaObject(teklaObject, rootObjectCollection, projectId);
        results.Add(result);

        ++count;
        onOperationProgressed.Report(new("Converting", (double)count / teklaObjects.Count));
        await Task.Yield();
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    var renderMaterialProxies = _materialUnpacker.UnpackRenderMaterial(teklaObjects.ToList());
    if (renderMaterialProxies.Count > 0)
    {
      rootObjectCollection[ProxyKeys.RENDER_MATERIAL] = renderMaterialProxies;
    }

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private SendConversionResult ConvertTeklaObject(
    TSM.ModelObject teklaObject,
    Collection collectionHost,
    string projectId
  )
  {
    string applicationId = teklaObject.GetSpeckleApplicationId();
    string sourceType = teklaObject.GetType().Name;

    try
    {
      Base converted;
      if (_sendConversionCache.TryGetValue(projectId, applicationId, out ObjectReference? value))
      {
        converted = value;
      }
      else
      {
        converted = _rootToSpeckleConverter.Convert(teklaObject);
      }

      var collection = _sendCollectionManager.GetAndCreateObjectHostCollection(teklaObject, collectionHost);

      // Add to host collection
      collection.elements.Add(converted);

      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
