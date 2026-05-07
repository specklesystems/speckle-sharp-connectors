using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.MicroStation.Plugin;
using Speckle.Converter.MicroStation.Settings;
using Speckle.Converters.Common;
using Speckle.Objects.Data;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Per-element conversion pipeline: dispatches to the geometric converter, extracts COM Element
/// metadata, and wraps both into the per-product <see cref="DataObject"/> subtype
/// (<c>MicroStationDataObject</c> / <c>OpenRoadsDataObject</c> / <c>OpenBridgeDataObject</c>) via
/// <see cref="SpeckleAddInIdentity.CreateDataObject"/>. Each product's identity file determines
/// the concrete DataObject type at compile time.
/// </summary>
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

    var units = converterSettings.Current.SpeckleUnits;
    var rootCollection = new Collection
    {
      name = docName,
      ["units"] = units,
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
        Base converted;
        if (sendConversionCache.TryGetValue(appId, projectId, out ObjectReference? cached))
        {
          converted = cached;
        }
        else
        {
          // Geometric conversion (top-level converter or bounding-box mesh fallback).
          var geometry = rootToSpeckleConverter.Convert(element);

          // Per-element metadata (level, color, line style, etc.) → properties dict.
          var properties = MicroStationElementPropertiesExtractor.Extract(element);

          // Wrap into the per-product DataObject (MicroStationDataObject / OpenRoadsDataObject / OpenBridgeDataObject).
          converted = SpeckleAddInIdentity.CreateDataObject(
            typeName: sourceType,
            displayValue: new List<Base> { geometry },
            properties: properties,
            units: units,
            applicationId: appId
          );
        }

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
