using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3dShared.ToSpeckle;
using Speckle.Converters.Common;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Pipelines.Send.Artifacts;

namespace Speckle.Connectors.Civil3dShared.Operations.Send;

public class Civil3dArtifactRootObjectBuilder : AutocadArtifactRootObjectBuilder
{
  private readonly PropertySetDefinitionHandler _propertySetDefinitionHandler;

  public Civil3dArtifactRootObjectBuilder(
    IRootToSpeckleConverter converter,
    IConverterSettingsStore<AutocadConversionSettings> converterSettings,
    AutocadInstanceUnpacker instanceUnpacker,
    AutocadMaterialUnpacker materialUnpacker,
    AutocadColorUnpacker colorUnpacker,
    IThreadContext threadContext,
    IArtifactPipelineFactory artifactPipelineFactory,
    ISpeckleApplication speckleApplication,
    ILogger<AutocadArtifactRootObjectBuilder> logger,
    PropertySetDefinitionHandler propertySetDefinitionHandler
  )
    : base(
      converter,
      converterSettings,
      instanceUnpacker,
      materialUnpacker,
      colorUnpacker,
      threadContext,
      artifactPipelineFactory,
      speckleApplication,
      logger
    )
  {
    _propertySetDefinitionHandler = propertySetDefinitionHandler;
  }

  protected override void EmitAdditionalNodes(ObjectsArtifactPipeline pipeline)
  {
    if (_propertySetDefinitionHandler.DefinitionsByHandle.Count == 0)
    {
      return;
    }

    // eav.property_set_definitions is the schema catalog [bundle-spec `property_set_definitions`]: one row per
    // (set, field), values stay per-object in eav, attachment derived from the value paths. The legacy carrier
    // pseudo-object is NOT written — it would pollute the objects table with a synthetic row.
    EmitPropertySetDefinitionRows(pipeline);
  }

  private void EmitPropertySetDefinitionRows(ObjectsArtifactPipeline pipeline)
  {
    // Handle-addressed, so two same-named definitions both reach the file [ENG-9363]. Rows are emitted in
    // AUTHORED field order (the file's row-order-is-field-order contract).
    var emittedSetKeys = new HashSet<string>(StringComparer.Ordinal);
    foreach (var record in _propertySetDefinitionHandler.DefinitionsByHandle.Values)
    {
      string setName = record.Name;

      // set_key: the shared recipe in PropertySetDefinitionLadder.ComputeSetKey — receive compares
      // existing in-document definitions with the same code, so reuse-if-identical cannot drift.
      var keyFields = new List<(string Name, string? DataType, string? Unit)>();
      foreach (var fieldName in record.FieldOrder)
      {
        if (Field(record.Fields, fieldName) is Dictionary<string, object?> fdk)
        {
          keyFields.Add(
            (
              fieldName,
              Field(fdk, PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY) as string,
              Field(fdk, "units") as string
            )
          );
        }
      }
      string setKey = PropertySetDefinitionLadder.ComputeSetKey(setName, keyFields);

      // Same name, different fields → two sets, told apart by set_key. Two definitions that hash the same ARE
      // the same schema, so they collapse to one set of rows — the spec's dedupe-identical-schemas promise.
      if (!emittedSetKeys.Add(setKey))
      {
        continue;
      }

      // Absent = apply-to-all, which is what a NULL applies_to means. Deliberately NOT part of set_key: the
      // recipe is shared with dwgextract, which cannot enumerate the filter at all [ENG-9362].
      string? setDescription = record.Description;
      string? appliesTo = record.AppliesTo;
      // Observed-on-instance ids are name-keyed, so they can only serve as the fallback for the FIRST
      // definition of a given name; the per-record ids read off the definition cover all of them [ENG-9361].
      _propertySetDefinitionHandler.FieldBucketIds.TryGetValue(setName, out var bucketByFieldObserved);

      foreach (var fieldName in record.FieldOrder)
      {
        if (Field(record.Fields, fieldName) is not Dictionary<string, object?> fd)
        {
          continue;
        }
        // Defaults split per the file's exactly-one-of rule: bool → default_boolean, numeric non-bool →
        // default_double, anything else with content → default_string.
        object? defaultValue = Field(fd, PropertySetDefinitionHandler.PROP_DEF_DEFAULT_VALUE_KEY);
        bool? defaultBoolean = defaultValue is bool b ? b : null;
        double? defaultDouble =
          defaultBoolean is null && defaultValue is IConvertible c and not string
            ? Convert.ToDouble(c, System.Globalization.CultureInfo.InvariantCulture)
            : null;
        string? defaultString =
          defaultBoolean is null && defaultDouble is null && defaultValue?.ToString() is { Length: > 0 } dv ? dv : null;
        // The definition's own FieldBucketId covers every authored field; the ids observed on instance values
        // are the fallback for a throwing getter [ENG-9361].
        if (!record.FieldBucketIds.TryGetValue(fieldName, out string? bucketId))
        {
          bucketByFieldObserved?.TryGetValue(fieldName, out bucketId);
        }
        pipeline.AddPropertySetDefinition(
          setName,
          setKey,
          fieldName,
          bucketId, // null only if BOTH the definition getter threw and no instance value was seen
          Field(fd, PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY) as string,
          defaultString,
          defaultDouble,
          defaultBoolean,
          Field(fd, "units") as string,
          Field(fd, PropertySetDefinitionHandler.PROP_DEF_DESCRIPTION_KEY) as string,
          setDescription,
          appliesTo
        );
      }
    }
  }

  // netstandard2.0/net48-safe Dictionary lookup (no CollectionExtensions.GetValueOrDefault there).
  private static object? Field(Dictionary<string, object?> dict, string key) =>
    dict.TryGetValue(key, out object? v) ? v : null;
}
