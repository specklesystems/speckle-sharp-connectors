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
) : IRootObjectBuilder<MgdElement>
{
  public async Task<RootObjectBuilderResult> Build(
    IReadOnlyList<MgdElement> elements,
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

    var model = app?.ActiveModelReference;
    var units = converterSettings.Current.SpeckleUnits;
    var rootCollection = new Collection
    {
      name = docName,
      ["units"] = units,
    };

    var results = new List<SendConversionResult>(elements.Count);
    int processed = 0;

    foreach (var mgdElement in elements)
    {
      cancellationToken.ThrowIfCancellationRequested();

      // ElementId is a managed struct with implicit conversions to UInt64 / Int64 (no .Value
      // property); cast once and reuse for the cache key + applicationId + COM bridge below.
      var elementIdValue = (ulong)mgdElement.ElementId;
      var appId = elementIdValue.ToString();
      var sourceType = mgdElement.ElementType.ToString();

      try
      {
        Base converted;
        if (sendConversionCache.TryGetValue(appId, projectId, out ObjectReference? cached))
        {
          converted = cached;
        }
        else
        {
          // Geometric conversion (the must-succeed step) — dispatcher pattern-matches managed
          // element subtypes; bounding-box fallback inside guarantees a non-null result.
          var geometry = rootToSpeckleConverter.Convert(mgdElement);

          // Per-element metadata extraction is best-effort — must NOT sink the whole element.
          // It still goes through the COM Element surface (color, level, line style) which is
          // not yet migrated to managed; bridge from the managed element via
          // ModelReference.GetElementByID64(id). The bridge can return null (orphan / transient
          // element state) and individual property reads can throw on edge cases — both paths
          // fall back to an empty properties dict so the element still ships with geometry.
          Dictionary<string, object?> properties;
          try
          {
            var comBridge = model?.GetElementByID64((long)elementIdValue);
            properties =
              comBridge != null
                ? MicroStationElementPropertiesExtractor.Extract(comBridge)
                : new Dictionary<string, object?>();
          }
          catch (Exception propEx) when (!propEx.IsFatal())
          {
            logger.LogWarning(propEx, "Properties extraction failed for element {id}; shipping with empty properties.", appId);
            properties = new Dictionary<string, object?>();
          }

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
