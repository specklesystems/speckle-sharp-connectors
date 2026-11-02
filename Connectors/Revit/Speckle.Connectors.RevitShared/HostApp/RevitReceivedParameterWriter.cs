using System.Globalization;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Diagnostics;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Services;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Writes what a received object carries besides geometry onto the element the artefact receive baked for it: always
/// the source <c>applicationId</c> [ENG-11189], and with the opt-in receive setting every user-facing property
/// (Rhino user text and dictionaries, parameters from another model) as an instance parameter [ENG-10511].
/// </summary>
/// <remarks>
/// <para>Two phases, because a shared parameter is project-wide state: the bake <see cref="Stage"/>s each element's
/// values as it creates it, and <see cref="Apply"/> runs once at the end of the bake transaction — one swap of the
/// application's shared-parameter file, one definition lookup per name, one binding insert or extension per name
/// over the full category set, then the values. Doing it per element would cost a file swap and a binding walk per
/// value.</para>
/// <para><b>Existing parameters win [ENG-11190].</b> A name that is already on an element — built-in, project or
/// shared — is written to, never shadowed by a second definition of the same name; a value is coerced to that
/// parameter's storage type, so a user who prepared a Number parameter gets a number. Only a name that no element
/// of a category has gets a Speckle shared definition, typed from the values this receive carries.</para>
/// <para>Warnings never fail the receive: an element keeps its geometry whatever happens to its parameters.</para>
/// </remarks>
public sealed class RevitReceivedParameterWriter
{
  /// <summary>The instance parameter every received element carries, holding the source object's
  /// <c>applicationId</c>: the key a script needs to find the Speckle object behind a Revit element.</summary>
  public const string APPLICATION_ID_PARAMETER = "Speckle Application Id";

  private const string PARAMETERS_ROOT = "Parameters" + ArtefactUserProperties.PATH_DELIMITER;
  private const string VALUE_SUFFIX = ArtefactUserProperties.PATH_DELIMITER + "value";

  private static readonly HashSet<string> s_parameterScopes = new(StringComparer.Ordinal)
  {
    "Instance Parameters",
    "Type Parameters",
    "System Type Parameters",
  };

  // Consumed by the receive to pick the element's category (the Rhino → Revit mapper) [ENG-9337]; not user data.
  private static readonly HashSet<string> s_consumedKeys = new(StringComparer.Ordinal)
  {
    "category",
    "builtInCategory",
  };

  // The characters Revit refuses in a shared parameter name.
  private static readonly char[] s_illegalNameChars =
  [
    '\\',
    ':',
    '{',
    '}',
    '[',
    ']',
    '|',
    ';',
    '<',
    '>',
    '?',
    '~',
    '`',
  ];

  private static readonly Base s_reportSource = new()
  {
    applicationId = "received-parameters",
    id = "received-parameters",
  };

  [Flags]
  private enum ValueKinds
  {
    None = 0,
    Text = 1,
    Number = 2,
    YesNo = 4,
  }

  private sealed record StagedElement(ElementId Id, Dictionary<string, object?> Values);

  private sealed record BindingPlan(
    string Name,
    Definition? ExistingDefinition,
    InstanceBinding? ExistingBinding,
    List<Category> MissingCategories
  );

  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitParameterCreator _parameterCreator;
  private readonly ScalingServiceToHost _scalingService;
  private readonly ILogger<RevitReceivedParameterWriter> _logger;

  private readonly List<StagedElement> _staged = new();
  private readonly Dictionary<string, ValueKinds> _kindsByName = new(StringComparer.Ordinal);
  private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);

  // names this receive gave up on, so no later phase spends a lookup or a warning on them
  private readonly HashSet<string> _skippedNames = new(StringComparer.Ordinal);
  private int _valuesSet;
  private int _valuesSkipped;

  public RevitReceivedParameterWriter(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitParameterCreator parameterCreator,
    ScalingServiceToHost scalingService,
    ILogger<RevitReceivedParameterWriter> logger
  )
  {
    _converterSettings = converterSettings;
    _parameterCreator = parameterCreator;
    _scalingService = scalingService;
    _logger = logger;
  }

  /// <summary>
  /// Records the values <paramref name="element"/> will carry. Type-scoped properties first, instance-scoped second,
  /// so on a name collision the instance value wins — the same precedence Rhino applies [ENG-9136].
  /// </summary>
  public void Stage(Element element, string appId, PropertyView typeProperties, PropertyView properties)
  {
    var values = new Dictionary<string, object?>(StringComparer.Ordinal) { [APPLICATION_ID_PARAMETER] = appId };
    NoteKind(APPLICATION_ID_PARAMETER, appId);
    if (_converterSettings.Current.ReceivePropertiesAsParameters)
    {
      Collect(typeProperties, values);
      Collect(properties, values);
    }
    _staged.Add(new StagedElement(element.Id, values));
  }

  /// <summary>
  /// Writes every staged value. Must run inside the bake transaction, after every element has been created. Distinct
  /// problems are reported once each as <see cref="Status.WARNING"/> rows on the receive report.
  /// </summary>
  public void Apply(Document doc, ArtefactSessionLog session, HashSet<ReceiveConversionResult> conversionResults)
  {
    if (_staged.Count == 0)
    {
      return;
    }
    session.SetStat("parameterElements", _staged.Count);
    session.SetStat("parameterNames", _kindsByName.Count);
    try
    {
      ApplyCore(doc, session);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Writing received parameters failed");
      _warnings.Add($"Parameters could not be written: {ex.Message}");
    }
    session.SetStat("parameterValuesSet", _valuesSet);
    session.SetStat("parameterValuesSkipped", _valuesSkipped);
    foreach (var warning in _warnings)
    {
      conversionResults.Add(
        new ReceiveConversionResult(
          Status.WARNING,
          s_reportSource,
          null,
          "Parameter",
          new SpeckleException(warning),
          "Received parameters"
        )
      );
    }
    _staged.Clear();
    _kindsByName.Clear();
    _warnings.Clear();
    _skippedNames.Clear();
    _valuesSet = 0;
    _valuesSkipped = 0;
  }

  private void Collect(PropertyView properties, Dictionary<string, object?> values)
  {
    // names claimed in this scope: two keys that sanitise to one name within a scope is a clash to report, while a
    // name already claimed by the other scope is the instance-over-type override above.
    var sourceByName = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var kvp in ArtefactUserProperties.Filter(properties))
    {
      if (s_consumedKeys.Contains(kvp.Key))
      {
        continue;
      }
      var name = ParameterName(kvp.Key);
      if (name.Length == 0)
      {
        continue;
      }
      if (string.Equals(name, APPLICATION_ID_PARAMETER, StringComparison.OrdinalIgnoreCase))
      {
        _warnings.Add(
          $"Property '{kvp.Key}' was not written: '{APPLICATION_ID_PARAMETER}' is reserved for the source id"
        );
        continue;
      }
      if (sourceByName.TryGetValue(name, out var claimedBy))
      {
        _warnings.Add($"Properties '{claimedBy}' and '{kvp.Key}' both map to parameter '{name}'; '{claimedBy}' wins");
        continue;
      }
      sourceByName[name] = kvp.Key;
      values[name] = kvp.Value;
      NoteKind(name, kvp.Value);
    }
  }

  private void NoteKind(string name, object? value)
  {
    var kind = value switch
    {
      bool => ValueKinds.YesNo,
      double => ValueKinds.Number,
      _ => ValueKinds.Text,
    };
    _kindsByName[name] = _kindsByName.TryGetValue(name, out var kinds) ? kinds | kind : kind;
  }

  // A parameter published by another Revit model arrives as Parameters.<scope>.<group>.<name>.value: the bare name is
  // what a Revit user knows it by, and what an existing parameter is matched on [ENG-11190]. Any other `x.value`
  // record (Rhino's area/volume) collapses the same way.
  private static string ParameterName(string key)
  {
    var name = key;
    if (name.StartsWith(PARAMETERS_ROOT, StringComparison.Ordinal))
    {
      var parts = name.Split(['.'], 4);
      if (parts.Length == 4 && s_parameterScopes.Contains(parts[1]))
      {
        name = parts[3];
      }
    }
    if (name.EndsWith(VALUE_SUFFIX, StringComparison.Ordinal) && name.Length > VALUE_SUFFIX.Length)
    {
      name = name[..^VALUE_SUFFIX.Length];
    }
    return Sanitize(name);
  }

  private static string Sanitize(string name)
  {
    char[]? buffer = null;
    for (int i = 0; i < name.Length; i++)
    {
      if (Array.IndexOf(s_illegalNameChars, name[i]) >= 0)
      {
        buffer ??= name.ToCharArray();
        buffer[i] = '_';
      }
    }
    return (buffer is null ? name : new string(buffer)).Trim();
  }

  private void ApplyCore(Document doc, ArtefactSessionLog session)
  {
    var elementsByCategory = GroupByCategory(doc);
    if (elementsByCategory.Count == 0)
    {
      return;
    }
    var plans = PlanBindings(doc, elementsByCategory);
    var newDefinitionByName = CreateDefinitions(doc, plans, session);
    ApplyBindings(doc, plans, newDefinitionByName, session);
    WriteValues(elementsByCategory);
  }

  // A category that cannot carry bound parameters keeps its geometry and costs one warning, not one per element.
  private Dictionary<Category, List<(Element Element, StagedElement Staged)>> GroupByCategory(Document doc)
  {
    var byId = new Dictionary<ElementId, (Category Category, List<(Element, StagedElement)> Group)>();
    foreach (var staged in _staged)
    {
      if (doc.GetElement(staged.Id) is not { } element)
      {
        _valuesSkipped += staged.Values.Count;
        continue;
      }
      var category = element.Category;
      if (category is null || !category.AllowsBoundParameters)
      {
        _warnings.Add(
          $"Category '{category?.Name ?? "(none)"}' does not accept parameters; its elements were received without any"
        );
        _valuesSkipped += staged.Values.Count;
        continue;
      }
      if (!byId.TryGetValue(category.Id, out var entry))
      {
        entry = (category, new List<(Element, StagedElement)>());
        byId[category.Id] = entry;
      }
      entry.Group.Add((element, staged));
    }
    return byId.Values.ToDictionary(e => e.Category, e => e.Group);
  }

  // A name is missing from a category as soon as one of its elements lacks it: a family parameter on one element
  // must not stand in for the others. An element that already has the name — built-in, project or shared — is
  // written to as it is, never shadowed.
  private List<BindingPlan> PlanBindings(
    Document doc,
    Dictionary<Category, List<(Element Element, StagedElement Staged)>> elementsByCategory
  )
  {
    var missingByName = new Dictionary<string, List<Category>>(StringComparer.Ordinal);
    foreach (var kv in elementsByCategory)
    {
      var lacking = new HashSet<string>(StringComparer.Ordinal);
      foreach (var (element, staged) in kv.Value)
      {
        foreach (var name in staged.Values.Keys)
        {
          if (!lacking.Contains(name) && element.LookupParameter(name) is null)
          {
            lacking.Add(name);
          }
        }
      }
      foreach (var name in lacking)
      {
        if (!missingByName.TryGetValue(name, out var categories))
        {
          categories = new List<Category>();
          missingByName[name] = categories;
        }
        categories.Add(kv.Key);
      }
    }

    var existingBindings = ReadBindings(doc);
    var plans = new List<BindingPlan>();
    foreach (var kv in missingByName)
    {
      if (!existingBindings.TryGetValue(kv.Key, out var existing))
      {
        plans.Add(new BindingPlan(kv.Key, null, null, kv.Value));
      }
      else if (existing.Binding is InstanceBinding instanceBinding)
      {
        plans.Add(new BindingPlan(kv.Key, existing.Definition, instanceBinding, kv.Value));
      }
      else
      {
        _warnings.Add($"'{kv.Key}' is bound as a type parameter in this project; instance values were not written");
        _skippedNames.Add(kv.Key);
      }
    }
    return plans;
  }

  // One file swap for the whole receive. A name Revit refuses (beyond the sanitised characters: length, a
  // case-variant of an existing definition) costs that name alone, never the pass.
  private Dictionary<string, ExternalDefinition> CreateDefinitions(
    Document doc,
    List<BindingPlan> plans,
    ArtefactSessionLog session
  )
  {
    var definitionByName = new Dictionary<string, ExternalDefinition>(StringComparer.Ordinal);
    var needDefinition = plans.FindAll(p => p.ExistingDefinition is null);
    if (needDefinition.Count == 0)
    {
      return definitionByName;
    }
    using var file = _parameterCreator.OpenSpeckleSharedParameterFile(doc.Application);
    foreach (var plan in needDefinition)
    {
      ExternalDefinition? definition = null;
      try
      {
        definition = RevitParameterCreator.GetOrCreateDefinition(
          file.Definitions,
          plan.Name,
          DataTypeFor(_kindsByName[plan.Name])
        );
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Could not define shared parameter '{Name}'", plan.Name);
      }
      if (definition is null)
      {
        _warnings.Add($"'{plan.Name}' could not be defined as a shared parameter");
        _skippedNames.Add(plan.Name);
        continue;
      }
      definitionByName[plan.Name] = definition;
    }
    session.SetStat("parameterDefinitions", definitionByName.Count);
    return definitionByName;
  }

  private void ApplyBindings(
    Document doc,
    List<BindingPlan> plans,
    Dictionary<string, ExternalDefinition> newDefinitionByName,
    ArtefactSessionLog session
  )
  {
    var bindingMap = doc.ParameterBindings;
    int inserted = 0;
    int extended = 0;
    foreach (var plan in plans)
    {
      if (_skippedNames.Contains(plan.Name))
      {
        continue;
      }
      bool bound = false;
      try
      {
        if (plan.ExistingBinding is { } binding && plan.ExistingDefinition is { } definition)
        {
          // a fresh set rather than the live one off the binding iterator, and the user's own parameter keeps the
          // group they filed it under
          var categories = new CategorySet();
          foreach (Category category in binding.Categories)
          {
            categories.Insert(category);
          }
          foreach (var category in plan.MissingCategories)
          {
            categories.Insert(category);
          }
          bound = bindingMap.ReInsert(definition, new InstanceBinding(categories), definition.GetGroupTypeId());
          extended += bound ? 1 : 0;
        }
        else
        {
          var categories = new CategorySet();
          foreach (var category in plan.MissingCategories)
          {
            categories.Insert(category);
          }
          bound = bindingMap.Insert(newDefinitionByName[plan.Name], new InstanceBinding(categories), GroupTypeId.Data);
          inserted += bound ? 1 : 0;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Could not bind parameter '{Name}'", plan.Name);
      }
      if (!bound)
      {
        _warnings.Add($"Revit rejected binding '{plan.Name}' to the received categories");
        _skippedNames.Add(plan.Name);
      }
    }
    session.SetStat("parameterBindingsInserted", inserted);
    session.SetStat("parameterBindingsExtended", extended);
    if (inserted + extended > 0)
    {
      doc.Regenerate();
    }
  }

  private void WriteValues(Dictionary<Category, List<(Element Element, StagedElement Staged)>> elementsByCategory)
  {
    foreach (var kv in elementsByCategory)
    {
      var categoryName = kv.Key.Name;
      foreach (var (element, staged) in kv.Value)
      {
        foreach (var value in staged.Values)
        {
          if (_skippedNames.Contains(value.Key))
          {
            _valuesSkipped++;
          }
          else if (TryWriteByName(element, value.Key, value.Value, out var reason))
          {
            _valuesSet++;
          }
          else
          {
            _warnings.Add($"'{value.Key}' on {categoryName} elements: {reason}");
            _valuesSkipped++;
          }
        }
      }
    }
  }

  private bool TryWriteByName(Element element, string name, object? value, out string reason)
  {
    try
    {
      var parameter = element.LookupParameter(name);
      if (parameter is null)
      {
        reason = "the parameter is not available; values were not written";
        return false;
      }
      if (parameter.IsReadOnly)
      {
        reason = "the parameter is read-only; values were not written";
        return false;
      }
      return TrySetTyped(parameter, value, out reason);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      reason = ex.Message;
      return false;
    }
  }

  // Every binding in the project by parameter name; the Definition is the one Revit holds, which ReInsert requires.
  private static Dictionary<string, (Definition Definition, Binding Binding)> ReadBindings(Document doc)
  {
    var map = new Dictionary<string, (Definition, Binding)>(StringComparer.Ordinal);
    var iterator = doc.ParameterBindings.ForwardIterator();
    while (iterator.MoveNext())
    {
      if (iterator.Key is { } definition && iterator.Current is Binding binding && !map.ContainsKey(definition.Name))
      {
        map[definition.Name] = (definition, binding);
      }
    }
    return map;
  }

  // The type of a definition this receive creates follows the values it carries; a name the Speckle file already
  // holds keeps its original type and the value is coerced on set instead.
  private static ForgeTypeId DataTypeFor(ValueKinds kinds) =>
    kinds switch
    {
      ValueKinds.YesNo => SpecTypeId.Boolean.YesNo,
      ValueKinds.Number => SpecTypeId.Number,
      _ => SpecTypeId.String.Text,
    };

  private bool TrySetTyped(Parameter parameter, object? value, out string reason)
  {
    reason = "";
    switch (parameter.StorageType)
    {
      case StorageType.String:
        if (parameter.Set(ArtefactUserProperties.Stringify(value)))
        {
          return true;
        }
        reason = "Revit rejected the text value";
        return false;

      case StorageType.Double:
        if (!TryNumber(value, out var number))
        {
          reason = "the parameter holds a number and the value is not one; it was not written";
          return false;
        }
        // A received value carries no units, so a measurable parameter reads it the way the user would type it:
        // in the project's display units for that kind of quantity.
        if (UnitUtils.IsMeasurableSpec(parameter.Definition.GetDataType()))
        {
          number = _scalingService.ScaleToNative(number, parameter.GetUnitTypeId());
        }
        if (parameter.Set(number))
        {
          return true;
        }
        reason = "Revit rejected the number";
        return false;

      case StorageType.Integer:
        if (TryInteger(value, out var integer))
        {
          if (parameter.Set(integer))
          {
            return true;
          }
          reason = "Revit rejected the value";
          return false;
        }
        reason = "the parameter holds an integer or yes/no and the value is neither; it was not written";
        return false;

      default:
        reason = "the parameter refers to another element and cannot be set from a value";
        return false;
    }
  }

  private static bool TryNumber(object? value, out double number)
  {
    switch (value)
    {
      case double d:
        number = d;
        return true;
      case bool b:
        number = b ? 1 : 0;
        return true;
      case string s:
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
      default:
        number = 0;
        return false;
    }
  }

  private static bool TryInteger(object? value, out int integer)
  {
    switch (value)
    {
      case bool b:
        integer = b ? 1 : 0;
        return true;
      case double d when d >= int.MinValue && d <= int.MaxValue:
        integer = (int)Math.Round(d);
        return true;
      case string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer):
        return true;
      case string s when IsYes(s):
        integer = 1;
        return true;
      case string s when IsNo(s):
        integer = 0;
        return true;
      default:
        integer = 0;
        return false;
    }
  }

  private static bool IsYes(string s) =>
    s.Equals("yes", StringComparison.OrdinalIgnoreCase) || s.Equals("true", StringComparison.OrdinalIgnoreCase);

  private static bool IsNo(string s) =>
    s.Equals("no", StringComparison.OrdinalIgnoreCase) || s.Equals("false", StringComparison.OrdinalIgnoreCase);
}
