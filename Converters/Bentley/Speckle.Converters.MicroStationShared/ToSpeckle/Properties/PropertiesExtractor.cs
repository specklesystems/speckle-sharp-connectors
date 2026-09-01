using System.Globalization;
using System.Text.RegularExpressions;
using Bentley.DgnPlatformNET.DgnEC;
using Bentley.ECObjects.Instance;
using Microsoft.Extensions.Logging;

namespace Speckle.Converters.MicroStation.ToSpeckle.Properties;

/// <summary>
/// The element property orchestrator — the managed port of dgnextract's <c>props_dgn.h</c> sources:
/// <list type="bullet">
/// <item><b>Level source</b>: <c>levelName</c> + <c>levelNumber</c> (DGN's Default level is 0); a
/// cell reports the level shared by the most descendant leaves when it sits on the Default level.</item>
/// <item><b>Item Types / EC-instance source</b>: every EC class instance attached to the element via
/// <see cref="DgnECManager.GetElementProperties"/>. Item-Type instances (schemas named
/// <c>DgnCustomItemTypes_&lt;library&gt;</c>) group under "Item Types"; application EC schemas group
/// under their class display label. Scalar leaves wrap as <c>{ value, name }</c>, arrays become
/// <c>"0","1",…</c> dictionaries, dates ISO-8601, EC XML-encoded names are decoded
/// (<c>__x0020__</c> → space). Multiple instances of one class are all kept (numbered keys).</item>
/// </list>
/// Error policy mirrors the managed orchestrator: each source is independently guarded — a failing
/// source never sinks the element.
/// </summary>
public class PropertiesExtractor(ILogger<PropertiesExtractor> logger)
{
  public PropertiesResult Extract(MgdElement element)
  {
    var props = new Dictionary<string, object?>();
    bool isCivil = false;
    try
    {
      ContributeLevelProps(element, props);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      logger.LogWarning(ex, "Level property source failed for element {Id}.", (ulong)element.ElementId);
    }
    try
    {
      isCivil = ContributeEcInstanceProps(element, props);
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      logger.LogWarning(ex, "EC property source failed for element {Id}.", (ulong)element.ElementId);
    }
    return new PropertiesResult(props, isCivil);
  }

  // ── Level source ─────────────────────────────────────────────────────────────────────────

  private static void ContributeLevelProps(MgdElement element, Dictionary<string, object?> props)
  {
    var (levelName, levelNumber) = GetLevelInfo(element);
    if (levelName != null)
    {
      props["levelName"] = levelName;
      props["levelNumber"] = levelNumber;
    }
  }

  /// <summary>The element's effective level (majority rule for cells) — also drives the layer
  /// collection an object files under.</summary>
  public static (string? Name, string? Number) GetLevelInfo(MgdElement element)
  {
    try
    {
      DPN.LevelCache? cache = element.DgnModelRef?.GetLevelCache();
      if (cache == null)
      {
        return (null, null);
      }
      DPN.LevelId levelId = ResolveEffectiveLevel(element);
      DPN.LevelHandle handle = cache.GetLevel(levelId, true);
      if (!handle.IsValid)
      {
        return (null, null);
      }
      return (handle.Name, handle.LevelCode.ToString());
    }
    catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
    {
      return (null, null);
    }
  }

  /// <summary>
  /// The element's own level — unless the element is a container (cell) whose own record commonly
  /// sits on the Default level while the content lives elsewhere: then the level shared by the most
  /// descendant graphics leaves wins (dgnextract's majority rule).
  /// </summary>
  private static DPN.LevelId ResolveEffectiveLevel(MgdElement element)
  {
    DPN.LevelId own = element.LevelId;
    if (element is not (MgdElements.CellHeaderElement or MgdElements.Type2Element))
    {
      return own;
    }

    var tally = new Dictionary<DPN.LevelId, int>();
    TallyDescendantLevels(element, tally, 0);
    if (tally.Count == 0)
    {
      return own;
    }
    DPN.LevelId best = own;
    int bestCount = -1;
    foreach (KeyValuePair<DPN.LevelId, int> entry in tally)
    {
      if (entry.Value > bestCount)
      {
        best = entry.Key;
        bestCount = entry.Value;
      }
    }
    return best;
  }

  private static void TallyDescendantLevels(MgdElement element, Dictionary<DPN.LevelId, int> tally, int depth)
  {
    if (depth > 8)
    {
      return;
    }
    MgdElements.ChildElementCollection? children = element.GetChildren();
    if (children == null)
    {
      return;
    }
    foreach (MgdElement? child in children)
    {
      if (child == null)
      {
        continue;
      }
      if (child.GetChildren() is { } grandchildren && grandchildren.Any(c => c != null))
      {
        TallyDescendantLevels(child, tally, depth + 1);
      }
      else if (child.IsGraphics)
      {
        DPN.LevelId levelId = child.LevelId;
        tally[levelId] = tally.TryGetValue(levelId, out int n) ? n + 1 : 1;
      }
    }
  }

  // ── Item Types / EC-instance source ──────────────────────────────────────────────────────

  private const string ITEM_TYPES_GROUP = "Item Types";
  private const string ITEM_TYPE_SCHEMA_PREFIX = "DgnCustomItemTypes_";

  /// <returns>True when the element carries Bentley Civil (OpenRoads) EC data.</returns>
  private bool ContributeEcInstanceProps(MgdElement element, Dictionary<string, object?> props)
  {
    using DgnECInstanceCollection? instances = DgnECManager.Manager.GetElementProperties(
      element,
      ECQueryProcessFlags.SearchAllExtrinsic | ECQueryProcessFlags.SearchItemTypes
    );
    if (instances == null)
    {
      return false;
    }

    var civilSeeds = new List<IDgnECInstance>();
    foreach (IDgnECInstance? instance in instances)
    {
      if (instance == null)
      {
        continue;
      }
      try
      {
        if (IsCivilSchema(instance.ClassDefinition?.Schema?.Name))
        {
          civilSeeds.Add(instance);
          continue; // civil internal flags aren't user-facing props; the aspect graph is (civil_dgn.h)
        }
        ContributeInstance(instance, props);
      }
      catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
      {
        logger.LogWarning(ex, "EC instance read failed on element {Id}.", (ulong)element.ElementId);
      }
    }

    if (civilSeeds.Count > 0)
    {
      try
      {
        CivilPropertiesSource.Contribute(civilSeeds, props);
      }
      catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
      {
        logger.LogWarning(ex, "Civil property source failed for element {Id}.", (ulong)element.ElementId);
      }
    }
    return civilSeeds.Count > 0;
  }

  internal static bool IsCivilSchema(string? schemaName) =>
    schemaName != null && schemaName.StartsWith("Bentley_Civil", StringComparison.Ordinal);

  private static void ContributeInstance(IDgnECInstance instance, Dictionary<string, object?> props)
  {
    var classDef = instance.ClassDefinition;
    if (classDef == null)
    {
      return;
    }
    string schemaName = classDef.Schema?.Name ?? "";
    string className = DecodeEcName(
      classDef.IsDisplayLabelDefined ? classDef.DisplayLabel : classDef.Name ?? "EC Instance"
    );

    Dictionary<string, object?>? values = ReadInstanceValues(instance);
    if (values == null)
    {
      return;
    }

    // Item-Type instances group under "Item Types"; application schemas under the class label.
    if (schemaName.StartsWith(ITEM_TYPE_SCHEMA_PREFIX, StringComparison.Ordinal))
    {
      Dictionary<string, object?> group = GetOrAddGroup(props, ITEM_TYPES_GROUP);
      AddKeepingAll(group, className, values);
    }
    else
    {
      AddKeepingAll(props, className, values);
    }
  }

  /// <summary>All non-null property values of one EC instance, wrapped — or null when empty.</summary>
  internal static Dictionary<string, object?>? ReadInstanceValues(IECInstance instance)
  {
    var values = new Dictionary<string, object?>();
    IEnumerator<IECPropertyValue> enumerator = instance.GetEnumerator(false, true);
    while (enumerator.MoveNext())
    {
      IECPropertyValue propertyValue = enumerator.Current;
      if (propertyValue == null || propertyValue.IsNull)
      {
        continue;
      }
      string name = DecodeEcName(DisplayNameOf(propertyValue));
      object? converted = ConvertEcValue(propertyValue);
      if (converted != null && !values.ContainsKey(name))
      {
        values[name] = Wrap(name, converted);
      }
    }
    return values.Count > 0 ? values : null;
  }

  private static string DisplayNameOf(IECPropertyValue propertyValue)
  {
    var property = propertyValue.Property;
    if (property != null)
    {
      return property.IsDisplayLabelDefined ? property.DisplayLabel : property.Name;
    }
    return propertyValue.AccessString;
  }

  private static object? ConvertEcValue(IECPropertyValue propertyValue)
  {
    if (propertyValue.IsArray)
    {
      var array = new Dictionary<string, object?>();
      int index = 0;
      IECValueContainer? contained = propertyValue.ContainedValues;
      if (contained != null)
      {
        foreach (IECPropertyValue? item in contained)
        {
          if (item is { IsNull: false })
          {
            object? value = ConvertEcValue(item);
            if (value != null)
            {
              array[index.ToString(CultureInfo.InvariantCulture)] = value;
            }
          }
          index++;
        }
      }
      return array.Count > 0 ? array : null;
    }

    if (propertyValue.IsStruct)
    {
      var members = new Dictionary<string, object?>();
      IECValueContainer? contained = propertyValue.ContainedValues;
      if (contained != null)
      {
        foreach (IECPropertyValue? member in contained)
        {
          if (member is { IsNull: false })
          {
            object? value = ConvertEcValue(member);
            if (value != null)
            {
              members[DecodeEcName(DisplayNameOf(member))] = value;
            }
          }
        }
      }
      return members.Count > 0 ? members : null;
    }

    if (!propertyValue.TryGetNativeValue(out object? native) || native == null)
    {
      return propertyValue.TryGetStringValue(out string? s) ? s : null;
    }
    return native switch
    {
      DateTime dt => dt.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
      bool or int or long or double or float or string => native,
      byte[] => null, // kBinary → skip (PropertyValue policy)
      _ => native.ToString(),
    };
  }

  /// <summary>Scalar wrap <c>{ value, name }</c> — the eav PropertyValue.Wrap shape (units omitted
  /// when unknown). Container values (dicts) pass through unwrapped.</summary>
  private static object Wrap(string name, object value) =>
    value is Dictionary<string, object?>
      ? value
      : new Dictionary<string, object?> { ["value"] = value, ["name"] = name };

  internal static Dictionary<string, object?> GetOrAddGroup(Dictionary<string, object?> props, string key)
  {
    if (props.TryGetValue(key, out object? existing) && existing is Dictionary<string, object?> dict)
    {
      return dict;
    }
    var group = new Dictionary<string, object?>();
    props[key] = group;
    return group;
  }

  /// <summary>An element can carry more than one instance of the same class; keep them all
  /// (numbered suffix on collisions, matching the managed extractor).</summary>
  internal static void AddKeepingAll(Dictionary<string, object?> target, string key, object value)
  {
    if (!target.ContainsKey(key))
    {
      target[key] = value;
      return;
    }
    for (int i = 2; ; i++)
    {
      string numbered = $"{key} ({i})";
      if (!target.ContainsKey(numbered))
      {
        target[numbered] = value;
        return;
      }
    }
  }

  private static readonly Regex s_ecNameEscape = new("__x([0-9A-Fa-f]{4})__", RegexOptions.Compiled);

  /// <summary>EC names are XML-encoded ("Test__x0020__Properties" → "Test Properties").</summary>
  internal static string DecodeEcName(string name) =>
    s_ecNameEscape.Replace(
      name,
      m => char.ConvertFromUtf32(int.Parse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture))
    );
}
