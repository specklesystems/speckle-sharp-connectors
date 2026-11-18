using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Creates new shared parameters on Revit elements and sets their value in one step.
/// Parameters are defined under a "Speckle" group in a Speckle-managed shared parameter
/// file, bound as Instance parameters under the requested built-in group, or "Data" when none resolves.
/// </summary>
public class RevitParameterCreator
{
  private const string SPECKLE_GROUP_NAME = "Speckle";
  private const string SHARED_PARAMS_FILENAME = "SpeckleSharedParameters.txt";

  private readonly RevitContext _revitContext;
  private readonly ILogger<RevitParameterCreator> _logger;
  private readonly Lazy<Dictionary<string, ForgeTypeId>> _groupsByLabel = new(BuildGroupsByLabel);

  public RevitParameterCreator(RevitContext revitContext, ILogger<RevitParameterCreator> logger)
  {
    _revitContext = revitContext;
    _logger = logger;
  }

  /// <summary>
  /// Creates the named parameter on the element's category (if it does not already exist)
  /// and sets its value. Must be called inside an open Revit transaction.
  /// </summary>
  /// <param name="groupLabel">The parameter group as the send side labels it (e.g. "Identity Data"); null for Data.</param>
  public UpdateResult CreateAndSet(Document doc, Element element, string paramName, string? groupLabel, object? value)
  {
    var app = _revitContext.UIApplication?.Application;
    if (app is null)
    {
      return UpdateResult.Fail("Could not access Revit application");
    }

    try
    {
      using var file = OpenSpeckleSharedParameterFile(app);
      return CreateAndSetCore(file.Definitions, doc, element, paramName, ResolveGroup(groupLabel), value);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to create parameter '{ParamName}' on element", paramName);
      return UpdateResult.Fail($"Exception creating parameter '{paramName}': {ex.Message}");
    }
  }

  /// <summary>
  /// Points the application at the Speckle shared parameter file and opens it; disposing restores whatever file
  /// the user had selected. Every definition created while the scope is open lands in the Speckle file, never in
  /// the user's own — the file swap is the one thing a caller must not get wrong.
  /// </summary>
  public SharedParameterFileScope OpenSpeckleSharedParameterFile(Autodesk.Revit.ApplicationServices.Application app)
  {
    var specklePath = GetSpeckleSharedParamsPath();
    if (!File.Exists(specklePath))
    {
      File.WriteAllText(specklePath, string.Empty);
    }
    return new SharedParameterFileScope(app, specklePath);
  }

  /// <summary>The definition named <paramref name="paramName"/> in the Speckle group, created with
  /// <paramref name="dataType"/> when absent. An existing definition keeps its type: the file is shared across
  /// receives and a definition's type cannot change once created.</summary>
  public static ExternalDefinition? GetOrCreateDefinition(
    DefinitionFile defFile,
    string paramName,
    ForgeTypeId dataType
  )
  {
    var group = defFile.Groups.get_Item(SPECKLE_GROUP_NAME) ?? defFile.Groups.Create(SPECKLE_GROUP_NAME);

    if (group.Definitions.get_Item(paramName) is ExternalDefinition existing)
    {
      return existing;
    }

    var opts = new ExternalDefinitionCreationOptions(paramName, dataType) { UserModifiable = true, Visible = true };

    return group.Definitions.Create(opts) as ExternalDefinition;
  }

  private static UpdateResult CreateAndSetCore(
    DefinitionFile defFile,
    Document doc,
    Element element,
    string paramName,
    ForgeTypeId group,
    object? value
  )
  {
    var definition = GetOrCreateDefinition(defFile, paramName, SpecTypeId.String.Text);
    if (definition is null)
    {
      return UpdateResult.Fail($"Could not create parameter definition for '{paramName}'");
    }

    var bindResult = EnsureCategoryBinding(doc, definition, element, group);
    if (!bindResult.IsSuccess)
    {
      return bindResult;
    }

    var parameter = element.LookupParameter(paramName);
    if (parameter is null)
    {
      return UpdateResult.Fail(
        $"Parameter '{paramName}' was registered but could not be found on element — try again after reloading the document"
      );
    }

    if (parameter.IsReadOnly)
    {
      return UpdateResult.Fail($"Parameter '{paramName}' is read-only");
    }

    return parameter.Set(value?.ToString() ?? string.Empty)
      ? UpdateResult.Success()
      : UpdateResult.Fail($"Failed to set value for parameter '{paramName}'");
  }

  private static string GetSpeckleSharedParamsPath()
  {
    var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    var speckleDir = Path.Combine(appData, "Speckle");
    Directory.CreateDirectory(speckleDir);
    return Path.Combine(speckleDir, SHARED_PARAMS_FILENAME);
  }

  /// <summary>
  /// The path's group segment is a group label in the host's UI language, as published on send, so it is matched
  /// by <see cref="LabelUtils.GetLabelForGroup"/> rather than by group id.
  /// </summary>
  private ForgeTypeId ResolveGroup(string? groupLabel)
  {
    if (string.IsNullOrWhiteSpace(groupLabel))
    {
      return GroupTypeId.Data;
    }

    if (_groupsByLabel.Value.TryGetValue(groupLabel!, out var group))
    {
      return group;
    }

    _logger.LogWarning("Unknown parameter group '{GroupLabel}', creating under Data", groupLabel);
    return GroupTypeId.Data;
  }

  private static Dictionary<string, ForgeTypeId> BuildGroupsByLabel()
  {
    // ungrouped parameters publish as "Other", the label of the empty group id, which GetAllBuiltInGroups omits
    var groups = new Dictionary<string, ForgeTypeId>();
    foreach (var group in ParameterUtils.GetAllBuiltInGroups().Append(new ForgeTypeId()))
    {
      var label = LabelUtils.GetLabelForGroup(group);
      if (!groups.ContainsKey(label))
      {
        groups[label] = group;
      }
    }

    return groups;
  }

  private static UpdateResult EnsureCategoryBinding(
    Document doc,
    ExternalDefinition definition,
    Element element,
    ForgeTypeId group
  )
  {
    var category = element.Category;
    if (category is null)
    {
      return UpdateResult.Fail("Element has no category — cannot bind parameter");
    }

    var bindingMap = doc.ParameterBindings;

    // Walk existing bindings: if this definition is already registered, expand the
    // category set if needed rather than inserting a duplicate.
    var iterator = bindingMap.ForwardIterator();
    while (iterator.MoveNext())
    {
      if (iterator.Key?.Name != definition.Name)
      {
        continue;
      }

      if (iterator.Current is not InstanceBinding existingBinding)
      {
        continue;
      }

      if (existingBinding.Categories.Contains(category))
      {
        return UpdateResult.Success();
      }

      existingBinding.Categories.Insert(category);
      // the binding is shared by every category already using the parameter, so a request on one element keeps
      // its current group rather than moving it for all of them (ENG-11170)
      bindingMap.ReInsert(definition, existingBinding, iterator.Key.GetGroupTypeId());
      return UpdateResult.Success();
    }

    // No binding yet — insert a fresh one scoped to this element's category.
    var categorySet = new CategorySet();
    categorySet.Insert(category);
    var binding = new InstanceBinding(categorySet);

    return bindingMap.Insert(definition, binding, group)
      ? UpdateResult.Success()
      : UpdateResult.Fail(
        $"Revit rejected the parameter binding for '{definition.Name}' on category '{category.Name}'"
      );
  }
}

/// <summary>The Speckle shared parameter file, open for the lifetime of the scope. See
/// <see cref="RevitParameterCreator.OpenSpeckleSharedParameterFile"/>.</summary>
public sealed class SharedParameterFileScope : IDisposable
{
  private readonly Autodesk.Revit.ApplicationServices.Application _app;
  private readonly string? _previousPath;

  internal SharedParameterFileScope(Autodesk.Revit.ApplicationServices.Application app, string specklePath)
  {
    _app = app;
    _previousPath = app.SharedParametersFilename;
    app.SharedParametersFilename = specklePath;
    try
    {
      Definitions =
        app.OpenSharedParameterFile()
        ?? throw new InvalidOperationException("Could not open or create the Speckle shared parameter file");
    }
    catch
    {
      app.SharedParametersFilename = _previousPath;
      throw;
    }
  }

  public DefinitionFile Definitions { get; }

  public void Dispose() => _app.SharedParametersFilename = _previousPath;
}
