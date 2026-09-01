using Autodesk.AutoCAD.DatabaseServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Receive;
using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Diagnostics;
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
    AutocadLayerBaker layerBaker,
    AutocadInstanceBaker instanceBaker,
    IAutocadMaterialBaker materialBaker,
    PropertySetBaker propertySetBaker
  )
    : base(
      converterSettings,
      converter,
      threadContext,
      autocadContext,
      activityFactory,
      logger,
      layerBaker,
      instanceBaker,
      materialBaker
    )
  {
    _propertySetBaker = propertySetBaker;
  }

  protected override void PreCleanAdditional(string baseLayerName) =>
    _propertySetBaker.PurgePropertySets(baseLayerName);

  protected override void ParseAndBakeAdditionalDefinitions(
    ArtefactBundle bundle,
    string baseLayerName,
    ArtefactSessionLog session
  )
  {
    // The definition ladder, most- to least-faithful. Tier 1: the eav.property_set_definitions file.
    if (PropertySetDefinitionLadder.FromSpecRows(bundle.PropertySetDefinitions) is { } fromFile)
    {
      _propertySetBaker.BakeSchemas(fromFile, baseLayerName);
      RecordDefinitionStats(session, "specRows");
      return;
    }
    // Tier 2: the legacy carrier pseudo-object (old managed bundles).
    if (FindDefinitions(bundle) is { } definitions)
    {
      _propertySetBaker.ParseAndBakePropertySetDefinitions(definitions, baseLayerName);
      RecordDefinitionStats(session, "carrier");
      return;
    }
    // Tier 3: neither shipped (e.g. dwgextract today) — synthesize minimal defs from the value rows so the
    // data still round-trips (types inferred, descriptions/defaults unrecoverable).
    if (PropertySetDefinitionLadder.SynthesizeFromValues(EnumeratePropertyTrees(bundle)) is { } synthesized)
    {
      _propertySetBaker.BakeSchemas(synthesized, baseLayerName);
      RecordDefinitionStats(session, "synthesis");
    }
  }

  // Property-set outcomes into the session ndjson/summary: the ILogger warnings only reach Seq, which made the
  // silent second-receive value loss undiagnosable offline [ENG-9328].
  private void RecordDefinitionStats(ArtefactSessionLog session, string tier)
  {
    session.SetStat($"propertySetDefs({tier})", _propertySetBaker.DefinitionCount);
    session.SetStat("propertySetDefsCreated", _propertySetBaker.DefinitionsCreated);
    session.SetStat("propertySetDefsReused", _propertySetBaker.DefinitionsReused);
  }

  // Only objects that actually carry property sets are materialized (ToNested) — the definition synthesis walks
  // the "Property Sets" subtree, and everything else stays columnar.
  private static IEnumerable<Dictionary<string, object?>> EnumeratePropertyTrees(ArtefactBundle bundle)
  {
    var table = bundle.PropertyTable!;
    foreach (int objK in table.KeysWith("properties.Property Sets").Distinct())
    {
      yield return table.Under(objK, "properties").ToNested();
    }
  }

  protected override void PostBakeEntity(
    Entity entity,
    PropertyView properties,
    Transaction tr,
    ArtefactSessionLog session
  )
  {
    var tree = properties.Under("properties");
    if (tree.Under("Property Sets").Count > 0)
    {
      // The baker walks the nested shape; materialize it for this entity only.
      session.Increment(
        _propertySetBaker.TryBakePropertySets(entity, tree.ToNested(), tr) ? "propertySetsBaked" : "propertySetsFailed"
      );
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
      var definitionsView = bundle
        .ObjectProperties(kv.Key)
        .Under("properties")
        .Under(ProxyKeys.PROPERTYSET_DEFINITIONS);
      if (definitionsView.Count > 0)
      {
        return definitionsView.ToNested();
      }
      return null;
    }
    return null;
  }
}
