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
    foreach (var setEntry in _propertySetDefinitionHandler.Definitions)
    {
      string setName = setEntry.Key;
      if (
        setEntry.Value.TryGetValue(PropertySetDefinitionHandler.PROP_SET_PROP_DEFS_KEY, out object? defsObj)
        && defsObj is Dictionary<string, object?> fieldDefs
      )
      {
        // set_key: SHA256 hex of the set name + every field's identity tuple, fields ordered by name — stable
        // across sends, distinguishes same-named definitions with different shapes.
        var keyParts = new List<string> { setName };
        foreach (var fieldName in fieldDefs.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
          if (fieldDefs[fieldName] is Dictionary<string, object?> fd)
          {
            keyParts.Add(
              $"{fieldName}|{Field(fd, PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY)}|{Field(fd, "units")}|{Field(fd, PropertySetDefinitionHandler.PROP_DEF_DEFAULT_VALUE_KEY)}"
            );
          }
        }
        string setKey;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
          byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", keyParts)));
          setKey = BitConverter.ToString(hash).Replace("-", ""); // net48-safe hex (no Convert.ToHexString)
        }

        foreach (var fieldEntry in fieldDefs)
        {
          if (fieldEntry.Value is not Dictionary<string, object?> fd)
          {
            continue;
          }
          object? defaultValue = Field(fd, PropertySetDefinitionHandler.PROP_DEF_DEFAULT_VALUE_KEY);
          double? defaultDouble = defaultValue is IConvertible c and not string and not bool
            ? Convert.ToDouble(c, System.Globalization.CultureInfo.InvariantCulture)
            : null;
          pipeline.AddPropertySetDefinition(
            setName,
            setKey,
            fieldEntry.Key,
            Field(fd, PropertySetDefinitionHandler.PROP_DEF_ID_KEY) as int?,
            Field(fd, PropertySetDefinitionHandler.PROP_DEF_TYPE_KEY) as string,
            defaultDouble is null ? defaultValue?.ToString() : null,
            defaultDouble,
            Field(fd, "units") as string,
            Field(fd, PropertySetDefinitionHandler.PROP_DEF_DESCRIPTION_KEY) as string,
            appliesTo: null // the handler does not capture applies-to yet; faithful recreate falls back to apply-to-all
          );
        }
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
