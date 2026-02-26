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
using Speckle.Sdk.Pipelines.Send;

namespace Speckle.Connectors.TeklaShared.Operations.Send;

/// <summary>
/// Continuous traversal builder for Tekla that streams objects through a <see cref="SendPipeline"/>
/// for packfile-based uploads. Same conversion logic as <see cref="TeklaRootObjectBuilder"/>.
/// </summary>
public class TeklaContinuousTraversalBuilder : IRootContinuousTraversalBuilder<TSM.ModelObject>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _converterSettings;
  private readonly SendCollectionManager _sendCollectionManager;
  private readonly ILogger<TeklaRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly TeklaMaterialUnpacker _materialUnpacker;

  public TeklaContinuousTraversalBuilder(
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
    SendPipeline sendPipeline,
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
        var result = await ConvertTeklaObject(teklaObject, rootObjectCollection, projectId, sendPipeline);
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

    // Process root collection and wait for all uploads
    await sendPipeline.Process(rootObjectCollection);
    await sendPipeline.WaitForUpload();

    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private async Task<SendConversionResult> ConvertTeklaObject(
    TSM.ModelObject teklaObject,
    Collection collectionHost,
    string projectId,
    SendPipeline sendPipeline
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

      var reference = await sendPipeline.Process(converted).ConfigureAwait(false);
      collection.elements.Add(reference);

      return new(Status.SUCCESS, applicationId, sourceType, reference);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to convert object {SourceType}", sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
