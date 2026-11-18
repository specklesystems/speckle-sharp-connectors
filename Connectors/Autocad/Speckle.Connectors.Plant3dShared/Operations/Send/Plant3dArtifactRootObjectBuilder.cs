using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using SpecContainer = Speckle.Bundle.Spec.Container;

namespace Speckle.Connectors.Autocad.Operations.Send;

public class Plant3dArtifactRootObjectBuilder(
  IRootToSpeckleConverter converter,
  IConverterSettingsStore<AutocadConversionSettings> converterSettings,
  AutocadInstanceUnpacker instanceUnpacker,
  AutocadMaterialUnpacker materialUnpacker,
  AutocadColorUnpacker colorUnpacker,
  IThreadContext threadContext,
  IArtifactPipelineFactory artifactPipelineFactory,
  ISpeckleApplication speckleApplication,
  ILogger<Plant3dArtifactRootObjectBuilder> logger
)
  : AutocadArtifactRootObjectBuilder(
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
  private static readonly string[] s_plant3dKeys = [PID_KEY, "Plant3D Data"];
  private const string PID_KEY = "P&ID";
  private const string DATA_MANAGER_KEY = "Data Manager";
  private const string LINE_GROUP_PREFIX = "p3d:group:";
  private const string LINE_NUMBER_KEY = "Line Number";
  private const string GROUP_ID_KEY = "Group Id";
  private const string GROUP_TYPE_KEY = "Group Type";
  private const string GROUP_TAG_KEY = "Group Tag";

  private readonly record struct LineGroup(int Id, string Type, string LineNumber, string? Tag)
  {
    public string Key => $"{LINE_GROUP_PREFIX}{Type}:{Id}";
    public string ReadableId => string.IsNullOrWhiteSpace(LineNumber) ? $"#{Id}" : LineNumber;
    public string Name => string.IsNullOrWhiteSpace(Tag) ? $"{Type} {ReadableId}" : Tag;
  }

  private sealed class SystemSlot(int containerK)
  {
    public int ContainerK { get; } = containerK;
    public int NextOrd { get; set; }
  }

  // ── Emit relations (worker thread) ──
  // Builds relations from the "Data Manager" properties written by Plant3dDataExtractor:
  //   IN_SYSTEM: part → "MEP System" container per line group, named from the members' "Group Tag".
  protected override void EmitVerticalRelations(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyList<CollectedObject> objects,
    HashSet<string> definitionMemberIds
  )
  {
    var systemsByKey = new Dictionary<string, SystemSlot>(StringComparer.Ordinal);

    // Deterministic order so IN_SYSTEM ords are stable across sends of the same model.
    foreach (var co in objects.OrderBy(co => co.ApplicationId, StringComparer.Ordinal))
    {
      if (
        definitionMemberIds.Contains(co.ApplicationId)
        || !TryGetDataManagerProperties(co.Properties, out var dataManagerProperties)
      )
      {
        continue;
      }

      var objK = pipeline.InternObject(co.ApplicationId);
      AddLineGroupAsSystem(pipeline, dataManagerProperties, systemsByKey, objK);
    }
  }

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManagerProperties,
    Dictionary<string, SystemSlot> systemsByKey,
    int objK
  )
  {
    if (!TryGetLineGroup(dataManagerProperties, out var group))
    {
      return;
    }

    if (!systemsByKey.TryGetValue(group.Key, out var slot))
    {
      var groupK = pipeline.AddContainer(group.Key, new SpecContainer(group.Name, null, "MEP System", null));
      systemsByKey[group.Key] = slot = new SystemSlot(groupK);
    }

    pipeline.InSystem(objK, slot.ContainerK, slot.NextOrd++);
  }

  // ── Shared helpers ──

  private static bool TryGetLineGroup(IReadOnlyDictionary<string, object?> dataManagerProperties, out LineGroup group)
  {
    if (
      !dataManagerProperties.TryGetValue(GROUP_ID_KEY, out var gid)
      || gid is not int id
      || id <= 0
      || !dataManagerProperties.TryGetValue(GROUP_TYPE_KEY, out var gt)
      || gt is not string type
      || string.IsNullOrWhiteSpace(type)
    )
    {
      group = default;
      return false;
    }

    var lineNumber = dataManagerProperties.GetValueOrDefault(LINE_NUMBER_KEY)?.ToString() ?? "";
    var tag = dataManagerProperties.GetValueOrDefault(GROUP_TAG_KEY)?.ToString();
    group = new LineGroup(id, type, lineNumber, tag);
    return true;
  }

  private static bool TryGetDataManagerProperties(
    IReadOnlyDictionary<string, object?> properties,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dataManagerProperties
  )
  {
    foreach (var plant3dKey in s_plant3dKeys)
    {
      if (
        TryGetDictionary(properties, plant3dKey, out var propertyGroup)
        && TryGetDictionary(propertyGroup, DATA_MANAGER_KEY, out var value)
      )
      {
        dataManagerProperties = value;
        return true;
      }
    }

    dataManagerProperties = null;
    return false;
  }

  private static bool TryGetDictionary(
    IReadOnlyDictionary<string, object?> properties,
    string key,
    [NotNullWhen(true)] out IReadOnlyDictionary<string, object?>? dictionary
  )
  {
    dictionary = properties.TryGetValue(key, out var value) ? value as IReadOnlyDictionary<string, object?> : null;
    return dictionary is not null;
  }
}
