using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Converters.Common;
using Speckle.Converters.CSiShared;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.CSiShared.Builders;

public class CsiRootObjectBuilder : IRootObjectBuilder<ICsiWrapper>
{
  private readonly IRootToSpeckleConverter _rootToSpeckleConverter;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly IConverterSettingsStore<CsiConversionSettings> _converterSettings;
  private readonly CsiSendCollectionManager _sendCollectionManager;
  private readonly ILogger<CsiRootObjectBuilder> _logger;
  private readonly ISdkActivityFactory _activityFactory;
  private readonly ICsiApplicationService _csiApplicationService;

  public CsiRootObjectBuilder(
    IRootToSpeckleConverter rootToSpeckleConverter,
    ISendConversionCache sendConversionCache,
    IConverterSettingsStore<CsiConversionSettings> converterSettings,
    CsiSendCollectionManager sendCollectionManager,
    ILogger<CsiRootObjectBuilder> logger,
    ISdkActivityFactory activityFactory,
    ICsiApplicationService csiApplicationService
  )
  {
    _sendConversionCache = sendConversionCache;
    _converterSettings = converterSettings;
    _sendCollectionManager = sendCollectionManager;
    _rootToSpeckleConverter = rootToSpeckleConverter;
    _logger = logger;
    _activityFactory = activityFactory;
    _csiApplicationService = csiApplicationService;
  }

  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<ICsiWrapper> csiObjects,
    SendInfo sendInfo,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken = default
  )
  {
    using var activity = _activityFactory.Start("Build");

    string modelFileName = _csiApplicationService.SapModel.GetModelFilename(false) ?? "Unnamed model";
    Collection rootObjectCollection = new() { name = modelFileName };
    rootObjectCollection["units"] = _converterSettings.Current.SpeckleUnits;

    List<SendConversionResult> results = new(csiObjects.Count);
    int count = 0;

    using (var _ = _activityFactory.Start("Convert all"))
    {
      foreach (ICsiWrapper csiObject in csiObjects)
      {
        using var _2 = _activityFactory.Start("Convert");
        cancellationToken.ThrowIfCancellationRequested();

        var result = ConvertCSiObject(csiObject, rootObjectCollection, sendInfo.ProjectId);
        results.Add(result);

        count++;
        onOperationProgressed.Report(new("Converting", (double)count / csiObjects.Count));
      }
    }

    if (results.All(x => x.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    await Task.Yield();
    return new RootObjectBuilderResult(rootObjectCollection, results);
  }

  private SendConversionResult ConvertCSiObject(ICsiWrapper csiObject, Collection typeCollection, string projectId)
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
        var result = _rootToSpeckleConverter.Convert(csiObject);
        if (result.IsFailure)
        {
          return new(Status.ERROR, applicationId, sourceType, result.Message);
        }

        converted = result.Value;
      }

      var collection = _sendCollectionManager.AddObjectCollectionToRoot(converted, typeCollection);
      collection.elements ??= new List<Base>();
      collection.elements.Add(converted);

      return new(Status.SUCCESS, applicationId, sourceType, converted, null);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, sourceType);
      return new(Status.ERROR, applicationId, sourceType, null, ex);
    }
  }
}
