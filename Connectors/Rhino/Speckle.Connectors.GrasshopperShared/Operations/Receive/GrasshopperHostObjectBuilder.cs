using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.GrasshopperShared.Operations.Receive;

public sealed class GrasshopperReceiveConversionResult : ReceiveConversionResult
{
  public object? Result { get; set; }
  public Base Source { get; set; }

  public GrasshopperReceiveConversionResult(
    Status status,
    Base source,
    object? result,
    string? resultId = null,
    string? resultType = null,
    Exception? exception = null
  )
    : base(status, source, resultId, resultType, exception)
  {
    Result = result;
    Source = source;
  }
}

public class GrasshopperHostObjectBuilder : IHostObjectBuilder
{
  private readonly IRootToHostConverter _converter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly TraversalContextUnpacker _contextUnpacker;
  private readonly RootObjectUnpacker _rootObjectUnpacker;
  private readonly ISdkActivityFactory _activityFactory;

  public GrasshopperHostObjectBuilder(
    IRootToHostConverter converter,
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    RootObjectUnpacker rootObjectUnpacker,
    ISdkActivityFactory activityFactory,
    TraversalContextUnpacker contextUnpacker
  )
  {
    _converter = converter;
    _converterSettings = converterSettings;
    _rootObjectUnpacker = rootObjectUnpacker;
    _activityFactory = activityFactory;
    _contextUnpacker = contextUnpacker;
  }

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
  public async Task<HostObjectBuilderResult> Build(
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    Base rootObject,
    string projectName,
    string modelName,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = _activityFactory.Start("Build");
    // POC: This is where the top level base-layer name is set. Could be abstracted or injected in the context?
    var baseLayerName = $"Project {projectName}: Model {modelName}";

    // 1 - Unpack objects and proxies from root commit object

    var unpackedRoot = _rootObjectUnpacker.Unpack(rootObject);

    // 2 - Split atomic objects and instance components with their path
    var (atomicObjects, instanceComponents) = _rootObjectUnpacker.SplitAtomicObjectsAndInstances(
      unpackedRoot.ObjectsToConvert
    );

    var atomicObjectsWithPath = _contextUnpacker.GetAtomicObjectsWithPath(atomicObjects);
    var instanceComponentsWithPath = _contextUnpacker.GetInstanceComponentsWithPath(instanceComponents);

    // 2.1 - these are not captured by traversal, so we need to re-add them here
    if (unpackedRoot.DefinitionProxies != null && unpackedRoot.DefinitionProxies.Count > 0)
    {
      var transformed = unpackedRoot.DefinitionProxies.Select(proxy =>
        (Array.Empty<Collection>(), proxy as IInstanceComponent)
      );
      instanceComponentsWithPath.AddRange(transformed);
    }

    // 3 - Get materials and colors, as they will be stored by layers and objects
    onOperationProgressed.Report(new("Converting materials and colors", null));
    if (unpackedRoot.RenderMaterialProxies != null)
    {
      using var _ = _activityFactory.Start("Render Materials");

      //_materialBaker.BakeMaterials(unpackedRoot.RenderMaterialProxies, baseLayerName);
    }

    if (unpackedRoot.ColorProxies != null)
    {
      //ParseColors(unpackedRoot.ColorProxies);
    }

    // 5 - Convert atomic objects
    List<ReceiveConversionResult> conversionResults = new();
    Dictionary<string, List<string>> applicationIdMap = new(); // This map is used in converting blocks in stage 2. keeps track of original app id => resulting new app ids post baking

    int count = 0;
    using (var _ = _activityFactory.Start("Converting objects"))
    {
      foreach (var (path, obj) in atomicObjectsWithPath)
      {
        using (var convertActivity = _activityFactory.Start("Converting object"))
        {
          onOperationProgressed.Report(new("Converting objects", (double)++count / atomicObjects.Count));
          try
          {
            // 2: convert
            var result = _converter.Convert(obj);

            // 4: log
            conversionResults.Add(
              new GrasshopperReceiveConversionResult(Status.SUCCESS, obj, result, null, result.GetType().ToString())
            );

            convertActivity?.SetStatus(SdkActivityStatusCode.Ok);
          }
          catch (Exception ex) when (!ex.IsFatal())
          {
            // TODO: No conversion report yet
            conversionResults.Add(new GrasshopperReceiveConversionResult(Status.ERROR, obj, null, null, null, ex));
            convertActivity?.SetStatus(SdkActivityStatusCode.Error);
            convertActivity?.RecordException(ex);
          }
        }
      }
    }

    // 6 - Convert instances
    using (var _ = _activityFactory.Start("Converting instances"))
    {
      // TODO: No instances yet
      // var (createdInstanceIds, consumedObjectIds, instanceConversionResults) = await _instanceBaker
      //   .BakeInstances(instanceComponentsWithPath, applicationIdMap, baseLayerName, onOperationProgressed)
      //   .ConfigureAwait(false);

      // TODO: No conversion report yet
      // conversionResults.RemoveAll(result => result.ResultId != null && consumedObjectIds.Contains(result.ResultId)); // remove all conversion results for atomic objects that have been consumed (POC: not that cool, but prevents problems on object highlighting)
      // conversionResults.AddRange(instanceConversionResults); // add instance conversion results to our list
    }

    // 7 - Create groups
    if (unpackedRoot.GroupProxies is not null)
    {
      // TODO: No groups yet
      // _groupBaker.BakeGroups(unpackedRoot.GroupProxies, applicationIdMap, baseLayerName);
    }

    return new HostObjectBuilderResult([], conversionResults);
  }
}
