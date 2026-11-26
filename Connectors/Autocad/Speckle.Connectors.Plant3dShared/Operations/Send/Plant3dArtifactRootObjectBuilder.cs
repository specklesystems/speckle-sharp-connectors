using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Converters.Plant3dShared.ToSpeckle;
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
  private const string LINE_GROUP_PREFIX = "p3d:group:";

  private static string GetSystemKey(Plant3dLineGroup group) => $"{LINE_GROUP_PREFIX}{group.Type}:{group.Id}";

  private static string GetSystemName(Plant3dLineGroup group) =>
    !string.IsNullOrWhiteSpace(group.Tag) ? group.Tag
    : !string.IsNullOrWhiteSpace(group.LineNumber) ? $"{group.Type} {group.LineNumber}"
    : $"{group.Type} #{group.Id}";

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
        || !Plant3dDataManagerProperties.TryReadLineGroup(co.Properties, out var group)
      )
      {
        continue;
      }

      var objK = pipeline.InternObject(co.ApplicationId);
      AddLineGroupAsSystem(pipeline, group, systemsByKey, objK);
    }
  }

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    Plant3dLineGroup group,
    Dictionary<string, SystemSlot> systemsByKey,
    int objK
  )
  {
    var key = GetSystemKey(group);
    if (!systemsByKey.TryGetValue(key, out var slot))
    {
      var name = GetSystemName(group);
      var groupK = pipeline.AddContainer(key, new SpecContainer(name, null, "MEP System", null));
      systemsByKey[key] = slot = new SystemSlot(groupK);
    }

    pipeline.InSystem(objK, slot.ContainerK, slot.NextOrd++);
  }
}
