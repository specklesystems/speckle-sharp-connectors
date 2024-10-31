using Microsoft.Extensions.Logging;
using Speckle.Connector.Tekla2024.Extensions;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converter.Tekla2024;
using Speckle.Converter.Tekla2024.ToSpeckle.Helpers;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Task = System.Threading.Tasks.Task;

namespace Speckle.Connector.Tekla2024.Operations.Send;

public class TeklaRootObjectBuilder : IRootObjectBuilder<TSM.ModelObject>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<TeklaConversionSettings> _converterSettings;
  private readonly ILogger<TeklaRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ColorHandler _colorHandler;

  public TeklaRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<TeklaConversionSettings> converterSettings,
    ILogger<TeklaRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ColorHandler colorHandler
  )
  {
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _colorHandler = colorHandler;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<TSM.ModelObject> teklaObjects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken = default
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
        using var _2 = _activityFactory.Start("Convert");
        cancellationToken.ThrowIfCancellationRequested();

        var result = ConvertTeklaObject(teklaObject, rootObjectCollection, sendInfo.ProjectId);
        results.Add(result);

        ++count;
        onOperationProgressed.Report(new("Converting", (double)count / teklaObjects.Count));
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    // add colors to root collection
    if (_colorHandler.ColorProxiesCache.Count > 0)
    {
      rootObjectCollection[ProxyKeys.COLOR] = _colorHandler.ColorProxiesCache.Values.ToList();
    }

    await Task.Yield();
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

      // Add to host collection
      collectionHost.elements.Add(converted);

      return new(Status.SUCCESS, applicationId, sourceType, converted);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
