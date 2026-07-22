using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Receive;
using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Civil3dShared.Operations.Receive;

public class Civil3dHostObjectArtefactBuilder : AutocadHostObjectArtefactBuilder
{
  private readonly PropertySetBaker _propertySetBaker;

  public Civil3dHostObjectArtefactBuilder(
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
    IRootToHostConverter converter,
    IThreadContext threadContext,
    AutocadContext autocadContext,
    ISdkActivityFactory activityFactory,
    ILogger<AutocadHostObjectArtefactBuilder> logger,
    PropertySetBaker propertySetBaker
  )
    : base(converterSettings, converter, threadContext, autocadContext, activityFactory, logger)
  {
    _propertySetBaker = propertySetBaker;
  }

  protected override void PreCleanAdditional(string baseLayerName) =>
    _propertySetBaker.PurgePropertySets(baseLayerName);

  protected override void ParseAndBakeAdditionalDefinitions(ArtefactBundle bundle, string baseLayerName)
  {
    if (FindDefinitions(bundle) is { } definitions)
    {
      _propertySetBaker.ParseAndBakePropertySetDefinitions(definitions, baseLayerName);
    }
  }

  protected override void PostBakeEntity(Entity entity, Dictionary<string, object?>? properties, Transaction tr)
  {
    if (
      properties is not null
      && properties.TryGetValue("properties", out var tree)
      && tree is Dictionary<string, object?> propertyTree
    )
    {
      _propertySetBaker.TryBakePropertySets(entity, propertyTree, tr);
    }
  }

  private static Dictionary<string, object?>? FindDefinitions(ArtefactBundle bundle)
  {
    foreach (var kv in bundle.ObjectAppIds)
    {
      if (kv.Value != PropertySetBaker.DEFINITIONS_CARRIER_APP_ID)
      {
        continue;
      }
      if (
        bundle.Properties.TryGetValue(kv.Key, out var props)
        && props.TryGetValue("properties", out var treeObj)
        && treeObj is Dictionary<string, object?> tree
        && tree.TryGetValue(ProxyKeys.PROPERTYSET_DEFINITIONS, out var defsObj)
        && defsObj is Dictionary<string, object?> definitions
      )
      {
        return definitions;
      }
      return null;
    }
    return null;
  }
}
