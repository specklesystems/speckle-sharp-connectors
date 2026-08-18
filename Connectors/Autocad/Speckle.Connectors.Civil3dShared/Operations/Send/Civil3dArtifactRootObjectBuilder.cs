using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Civil3dShared.HostApp;
using Speckle.Connectors.Common.Operations;
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
    if (_propertySetDefinitionHandler.Definitions.Count == 0)
    {
      return;
    }

    var definitions = new Dictionary<string, object?>();
    foreach (var kvp in _propertySetDefinitionHandler.Definitions)
    {
      definitions[kvp.Key] = kvp.Value;
    }

    var properties = new Dictionary<string, object?> { [ProxyKeys.PROPERTYSET_DEFINITIONS] = definitions };
    pipeline.InternObject(PropertySetBaker.DEFINITIONS_CARRIER_APP_ID);
    pipeline.AddProperties(PropertySetBaker.DEFINITIONS_CARRIER_APP_ID, properties);

    EmitPropertySetDefinitionRows(pipeline);
  }

  // eav.property_set_definitions is the schema catalog going forward [bundle-spec `property_set_definitions`]:
  // one row per (set, field), values stay per-object in eav, attachment derived from the value paths. The carrier
  // pseudo-object above keeps being written this release so pre-vocab receivers still rebuild definitions.
  private void EmitPropertySetDefinitionRows(ObjectsArtifactPipeline pipeline)
  {
#if SDK_BUNDLE_VOCAB_ADDITIONS
    // Requires Speckle.Objects ≥ speckle-sharp-sdk@oguzhan/bundle-vocab-additions (AddPropertySetDefinition API).
    // Rows are emitted in AUTHORED field order (the file's row-order-is-field-order contract).
    foreach (var setEntry in _propertySetDefinitionHandler.Definitions)
    {
      string setName = setEntry.Key;
      if (
        !setEntry.Value.TryGetValue(PropertySetDefinitionHandler.PROP_SET_PROP_DEFS_KEY, out object? defsObj)
        || defsObj is not Dictionary<string, object?> fieldDefs
        || !_propertySetDefinitionHandler.FieldOrders.TryGetValue(setName, out var fieldOrder)
      )
      {
        continue;
      }

      // set_key recipe (cross-producer, byte-exact — keep in sync with dwgextract):
      //   sha256_hex_uppercase( utf8( set_name + "\n"
      //     + join("\n", for each field in AUTHORED order: field_name + "|" + data_type + "|" + (unit ?? "")) ) )
      var keyParts = new List<string> { setName };
      foreach (var fieldName in fieldOrder)
      {
        if (Field(fieldDefs, fieldName) is Dictionary<string, object?> fdk)
        {
          keyParts.Add(
            $"{fieldName}|{Field(fdk, PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY)}|{Field(fdk, "units")}"
          );
        }
      }
      string setKey;
      using (var sha = System.Security.Cryptography.SHA256.Create())
      {
        byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keyParts)));
        setKey = BitConverter.ToString(hash).Replace("-", ""); // net48-safe hex (no Convert.ToHexString)
      }

      _propertySetDefinitionHandler.SetDescriptions.TryGetValue(setName, out string? setDescription);
      _propertySetDefinitionHandler.FieldBucketIds.TryGetValue(setName, out var bucketByField);

      foreach (var fieldName in fieldOrder)
      {
        if (Field(fieldDefs, fieldName) is not Dictionary<string, object?> fd)
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
          defaultBoolean is null && defaultDouble is null && defaultValue?.ToString() is { Length: > 0 } dv
            ? dv
            : null;
        string? bucketId = null;
        bucketByField?.TryGetValue(fieldName, out bucketId);
        pipeline.AddPropertySetDefinition(
          setName,
          setKey,
          fieldName,
          bucketId, // null when the definition was never attached to a sent object — receive matches by name
          Field(fd, PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY) as string,
          defaultString,
          defaultDouble,
          defaultBoolean,
          Field(fd, "units") as string,
          Field(fd, PropertySetDefinitionHandler.PROP_DEF_DESCRIPTION_KEY) as string,
          setDescription,
          appliesTo: null // only AppliesToAll is repo-confirmed; expected member: PropertySetDefinition.AppliesToFilter
        );
      }
    }
#else
    // No-op until the SDK vocab pin bump — define SDK_BUNDLE_VOCAB_ADDITIONS once Speckle.Objects ships
    // AddPropertySetDefinition (branch oguzhan/bundle-vocab-additions).
    _ = pipeline;
#endif
  }

#if SDK_BUNDLE_VOCAB_ADDITIONS
  // netstandard2.0/net48-safe Dictionary lookup (no CollectionExtensions.GetValueOrDefault there).
  private static object? Field(Dictionary<string, object?> dict, string key) =>
    dict.TryGetValue(key, out object? v) ? v : null;
#endif
}
