using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Data;
using Speckle.Objects.Utils;
using Speckle.Sdk;
using PPDL = Autodesk.ProcessPower.DataLinks;
using SpecContainer = Speckle.Bundle.Spec.Container;

namespace Speckle.Connectors.Autocad.Operations.Send;

public partial class AutocadArtifactRootObjectBuilder
{
  private static readonly string[] s_plant3dKeys = [PID_KEY, "Plant3D Data"];
  private const string PID_KEY = "P&ID";
  private const string DATA_MANAGER_KEY = "Data Manager";
  private const string CLASS_PREFIX = "p3d:class:";
  private const string LINE_GROUP_PREFIX = "p3d:group:";
  private const string LINE_GROUP_OBJECT_SUFFIX = ":object"; // keeps the group's object id distinct from its container id
  private const string LINE_NUMBER_KEY = "Line Number";
  private const string GROUP_ID_KEY = "Group Id";
  private const string GROUP_TYPE_KEY = "Group Type";
  private const string TAG_KEY = "Tag";

  private static string LevelKey(int level) => $"Level {level}";

  private readonly record struct LineGroup(int Id, string Type, string LineNumber)
  {
    public string Key => $"{LINE_GROUP_PREFIX}{Type}:{Id}";
    public string ReadableId => string.IsNullOrWhiteSpace(LineNumber) ? $"#{Id}" : LineNumber;
    public string Name => $"{Type} {ReadableId}";
    public string ObjectId => Key + LINE_GROUP_OBJECT_SUFFIX;
  }

  // ── Phase 1: collect line-group objects (UI thread) ──
  // No geometry; they carry the group's Data Links properties.
  partial void CollectVerticalObjects(List<CollectedObject> objects)
  {
    if (!TryGetDataLinksManager(out var dataLinksManager))
    {
      return;
    }

    var collectedGroupKeys = new HashSet<string>(StringComparer.Ordinal);
    var lineGroupObjects = new List<CollectedObject>();

    foreach (var co in objects)
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
      lineGroupObjects.Add(new CollectedObject(applicationId, group.Type, co.LayerName, properties, lineGroupObject));
    }

    objects.AddRange(lineGroupObjects);
  }

  private bool TryGetDataLinksManager([NotNullWhen(true)] out PPDL.DataLinksManager? dataLinksManager)
  {
    try
    {
      dataLinksManager = PPDL.DataLinksManager.GetManager(converterSettings.Current.Document.Database);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(ex, "Plant 3D DataLinksManager unavailable");
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
      logger.LogWarning(ex, "Failed to read Data Links properties for line group {GroupId}", rowId);
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
  partial void EmitVerticalRelations(
    ObjectsArtifactPipeline pipeline,
    CollectedModel model,
    HashSet<string> definitionMemberIds
  )
  {
    var lineGroupKByKey = new Dictionary<string, int>(StringComparer.Ordinal);
    var classKByPath = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var co in LineGroupObjectsFirst(model.Objects))
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
      AddLineGroupAsSystem(pipeline, dataManagerProperties, lineGroupKByKey, objK, systemName);
      AddClassHierarchyAsGroups(pipeline, dataManagerProperties, classKByPath, objK);
    }
  }

  /// <summary>
  /// Yields line-group objects before all other objects, so each "MEP System" container is created (and named)
  /// from its group object rather than from whichever member is seen first.
  /// </summary>
  private static IEnumerable<CollectedObject> LineGroupObjectsFirst(IEnumerable<CollectedObject> objects) =>
    objects.OrderBy(co => IsLineGroupObject(co) ? 0 : 1);

  private static void AddLineGroupAsSystem(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManagerProperties,
    Dictionary<string, int> lineGroupKByKey,
    int objK,
    string? systemName = null
  )
  {
    if (!TryGetLineGroup(dataManagerProperties, out var group))
    {
      return;
    }

    if (!lineGroupKByKey.TryGetValue(group.Key, out var groupK))
    {
      var name = string.IsNullOrWhiteSpace(systemName) ? group.Name : systemName;
      groupK = pipeline.AddContainer(group.Key, new SpecContainer(name, null, "MEP System", null));
      lineGroupKByKey[group.Key] = groupK;
    }

    pipeline.InSystem(objK, groupK, ord: 0);
  }

  private static void AddClassHierarchyAsGroups(
    ObjectsArtifactPipeline pipeline,
    IReadOnlyDictionary<string, object?> dataManagerProperties,
    Dictionary<string, int> classKByPath,
    int objK
  )
  {
    var levels = ReadClassLevels(dataManagerProperties);
    if (levels.Count == 0)
    {
      return;
    }

    int? parentK = null; // def_ref = parent container's node K (null for the root level)
    var leafK = 0;
    for (var i = 0; i < levels.Count; i++)
    {
      var path = CLASS_PREFIX + string.Join("|", levels.Take(i + 1));
      if (!classKByPath.TryGetValue(path, out var k))
      {
        k = pipeline.AddContainer(path, new SpecContainer(levels[i], parentK, "Group", null));
        classKByPath[path] = k;
      }
      parentK = leafK = k;
    }

    pipeline.InGroup(objK, leafK, ord: 0);
  }

  private static List<string> ReadClassLevels(IReadOnlyDictionary<string, object?> dataManagerProperties)
  {
    var levels = new List<string>();
    for (
      int i = 1;
      dataManagerProperties.TryGetValue(LevelKey(i), out var lvl) && lvl is string s && !string.IsNullOrWhiteSpace(s);
      i++
    )
    {
      levels.Add(s);
    }
    return levels;
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
