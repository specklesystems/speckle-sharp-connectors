using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.MicroStation.Plugin;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

public class MicroStationRootObjectBuilder(
  IRootToSpeckleConverter rootToSpeckleConverter,
  ISendConversionCache sendConversionCache,
  IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
  ILogger<MicroStationRootObjectBuilder> logger,
  ISdkActivityFactory activityFactory
) : IRootObjectBuilder<Element>
{
  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<Element> elements,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = activityFactory.Start("Build");

    if (elements.Count == 0)
    {
      throw new SpeckleException("No objects to convert.");
    }

    var app = MsApp.TryGetInstance();
    var docName =
      app?.HasActiveDesignFile == true ? System.IO.Path.GetFileName(app.ActiveDesignFile.FullName) : "Unnamed Model";

    var rootCollection = new Collection
    {
      name = docName,
      ["units"] = converterSettings.Current.SpeckleUnits,
    };

    var results = new List<SendConversionResult>(elements.Count);
    int processed = 0;

    foreach (var element in elements)
    {
      cancellationToken.ThrowIfCancellationRequested();

      var appId = element.ID.ToString();
      var sourceType = element.Type.ToString();

      try
      {
        Base converted = sendConversionCache.TryGetValue(appId, projectId, out ObjectReference? cached)
          ? cached
          : rootToSpeckleConverter.Convert(element);

        rootCollection.elements.Add(converted);
        results.Add(new SendConversionResult(Status.SUCCESS, appId, sourceType, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert element {id}", appId);
        results.Add(new SendConversionResult(Status.ERROR, appId, sourceType, null, ex));
      }

      onOperationProgressed.Report(new CardProgress("Converting", (double)++processed / elements.Count));
    }

    if (results.All(r => r.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    await Task.CompletedTask;
    return new RootObjectBuilderResult(rootCollection, results);
  }
}
