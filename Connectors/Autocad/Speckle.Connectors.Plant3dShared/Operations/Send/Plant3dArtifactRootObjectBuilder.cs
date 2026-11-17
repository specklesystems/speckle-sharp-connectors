using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Threading;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Pipelines.Send.Artifacts;
using PPDL = Autodesk.ProcessPower.DataLinks;
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
  ILogger<AutocadArtifactRootObjectBuilder> logger
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
  private const string LINE_GROUP_OBJECT_SUFFIX = ":object"; // keeps the group's object id distinct from its container id
  private const string LINE_NUMBER_KEY = "Line Number";
  private const string GROUP_ID_KEY = "Group Id";
  private const string GROUP_TYPE_KEY = "Group Type";
  private const string TAG_KEY = "Tag";

  private readonly IConverterSettingsStore<AutocadConversionSettings> _converterSettings = converterSettings;
  private readonly ILogger<AutocadArtifactRootObjectBuilder> _logger = logger;

  private readonly record struct LineGroup(int Id, string Type, string LineNumber)
  {
    public string Key => $"{LINE_GROUP_PREFIX}{Type}:{Id}";
    public string ReadableId => string.IsNullOrWhiteSpace(LineNumber) ? $"#{Id}" : LineNumber;
    public string Name => $"{Type} {ReadableId}";
    public string ObjectId => Key + LINE_GROUP_OBJECT_SUFFIX;
  }

  private sealed class SystemSlot(int containerK)
  {
    public int ContainerK { get; } = containerK;
    public int NextOrd { get; set; }
  }

  // ── Phase 1: collect line-group objects (UI thread) ──
  // No geometry; they carry the group's Data Links properties.
  protected override void CollectVerticalObjects(List<CollectedObject> objects)
  {
    if (!TryGetDataLinksManager(out var dataLinksManager))
    {
      return;
    }

    var collectedGroupKeys = new HashSet<string>(StringComparer.Ordinal);

    foreach (var co in objects.ToList())
    {
      if (
        !TryGetDataManagerProperties(co.Properties, out var dataManagerProperties)
        || !TryGetLineGroup(dataManagerProperties, out var group)
        || !collectedGroupKeys.Add(group.Key)
      )
      {
        continue;
      }

      var pidProperties = ReadDataLinksProperties(dataLinksManager, group.Id);
      pidProperties[DATA_MANAGER_KEY] = new Dictionary<string, object?>(StringComparer.Ordinal)
      {
        [GROUP_ID_KEY] = group.Id,
        [GROUP_TYPE_KEY] = group.Type,
        [LINE_NUMBER_KEY] = group.LineNumber,
      };

      var properties = new Dictionary<string, object?>(StringComparer.Ordinal) { [PID_KEY] = pidProperties };
      var applicationId = group.ObjectId;
      var lineGroupObject = new DataObject
      {
        name = ResolveLineGroupName(group, pidProperties),
        properties = properties,
        displayValue = [],
        applicationId = applicationId,
      };

      // Layer: the first member's layer keeps it in the default IN_COLLECTION scene view.
      objects.Add(new CollectedObject(applicationId, group.Type, co.LayerName, properties, lineGroupObject));
    }
  }

  private bool TryGetDataLinksManager([NotNullWhen(true)] out PPDL.DataLinksManager? dataLinksManager)
  {
    try
    {
      dataLinksManager = PPDL.DataLinksManager.GetManager(_converterSettings.Current.Document.Database);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Plant 3D DataLinksManager unavailable");
      dataLinksManager = null;
    }

    return dataLinksManager is not null;
  }

  // Must run on the UI thread (DataLinks is document-bound).
  private Dictionary<string, object?> ReadDataLinksProperties(PPDL.DataLinksManager dataLinksManager, int rowId)
  {
    var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
    try
    {
      var rowProperties = dataLinksManager.GetAllProperties(rowId, true);
      if (rowProperties is not null)
      {
        foreach (var kvp in rowProperties)
        {
          properties[kvp.Key ?? "unknown"] = kvp.Value;
        }
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to read Data Links properties for line group {GroupId}", rowId);
    }

    return properties;
  }

  private static string ResolveLineGroupName(LineGroup group, IReadOnlyDictionary<string, object?> pidProperties)
  {
    var tag = pidProperties.GetValueOrDefault(TAG_KEY)?.ToString();
    return string.IsNullOrWhiteSpace(tag) ? group.Name : tag;
  }

  // ── Phase 2: emit relations (worker thread) ──
  // Builds relations from the "Data Manager" properties written by Plant3dDataExtractor:
  //   IN_SYSTEM: part → "MEP System" container per line group; the group's own object joins it too.
  //   Class tree: part → leaf "Group" container; each level def_refs its parent.
  protected override void EmitVerticalRelations(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyList<CollectedObject> objects,
    HashSet<string> definitionMemberIds
  )
  {
    var systemsByKey = new Dictionary<string, SystemSlot>(StringComparer.Ordinal);

    foreach (var co in LineGroupObjectsFirst(objects))
    {
      if (
        definitionMemberIds.Contains(co.ApplicationId)
        || !TryGetDataManagerProperties(co.Properties, out var dataManagerProperties)
      )
      {
        continue;
      }

      var objK = pipeline.InternObject(co.ApplicationId);
      var systemName = IsLineGroupObject(co) ? (co.Converted as DataObject)?.name : null;
      AddLineGroupAsSystem(pipeline, dataManagerProperties, systemsByKey, objK, systemName);
    }
  }

  /// <summary>
  /// Line-group objects first (so each container is named from its group object), then members in a
  /// deterministic order so IN_SYSTEM ords are stable across sends of the same model.
  /// </summary>
  private static IEnumerable<CollectedObject> LineGroupObjectsFirst(IEnumerable<CollectedObject> objects) =>
    objects.OrderBy(co => IsLineGroupObject(co) ? 0 : 1).ThenBy(co => co.ApplicationId, StringComparer.Ordinal);

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManagerProperties,
    Dictionary<string, SystemSlot> systemsByKey,
    int objK,
    string? systemName = null
  )
  {
    if (!TryGetLineGroup(dataManagerProperties, out var group))
    {
      return;
    }

    if (!systemsByKey.TryGetValue(group.Key, out var slot))
    {
      var name = string.IsNullOrWhiteSpace(systemName) ? group.Name : systemName;
      var groupK = pipeline.AddContainer(group.Key, new SpecContainer(name, null, "MEP System", null));
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
    group = new LineGroup(id, type, lineNumber);
    return true;
  }

  private static bool IsLineGroupObject(CollectedObject co) =>
    co.Converted is DataObject
    && co.ApplicationId.StartsWith(LINE_GROUP_PREFIX, StringComparison.Ordinal)
    && co.ApplicationId.EndsWith(LINE_GROUP_OBJECT_SUFFIX, StringComparison.Ordinal);

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
