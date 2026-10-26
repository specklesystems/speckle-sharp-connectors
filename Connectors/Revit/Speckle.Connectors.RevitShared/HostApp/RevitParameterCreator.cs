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
/// file, bound as Instance Text parameters under the requested built-in group, or "Data" when none resolves.
/// </summary>
public class RevitParameterCreator
{
  private const string SPECKLE_GROUP_NAME = "Speckle";
  private const string SHARED_PARAMS_FILENAME = "SpeckleSharedParameters.txt";

  private readonly RevitContext _revitContext;
  private readonly ILogger<RevitParameterCreator> _logger;

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
  public UpdateResult CreateAndSet(
    Document doc,
    Element element,
    string paramName,
    string? groupLabel,
    object? value
  )
  {
    var app = _revitContext.UIApplication?.Application;
    if (app is null)
    {
      return UpdateResult.Fail("Could not access Revit application");
    }

    var specklePath = GetSpeckleSharedParamsPath();
    if (!File.Exists(specklePath))
    {
      File.WriteAllText(specklePath, string.Empty);
    }

    // Keep SharedParametersFilename pointing at our file for the entire creation
    // flow — including EnsureCategoryBinding — then restore it unconditionally.
    var previousPath = app.SharedParametersFilename;
    app.SharedParametersFilename = specklePath;
    try
    {
      return CreateAndSetCore(app, doc, element, paramName, ResolveGroup(groupLabel), value);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to create parameter '{ParamName}' on element", paramName);
      return UpdateResult.Fail($"Exception creating parameter '{paramName}': {ex.Message}");
    }
    finally
    {
      app.SharedParametersFilename = previousPath;
    }
  }

  private UpdateResult CreateAndSetCore(
    Autodesk.Revit.ApplicationServices.Application app,
    Document doc,
    Element element,
    string paramName,
    ForgeTypeId group,
    object? value
  )
  {
    var defFile = app.OpenSharedParameterFile();
    if (defFile is null)
    {
      return UpdateResult.Fail("Could not open or create the Speckle shared parameter file");
    }

    var definition = GetOrCreateDefinition(defFile, paramName);
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

  private ExternalDefinition? GetOrCreateDefinition(DefinitionFile defFile, string paramName)
  {
    var group = defFile.Groups.get_Item(SPECKLE_GROUP_NAME) ?? defFile.Groups.Create(SPECKLE_GROUP_NAME);

    if (group.Definitions.get_Item(paramName) is ExternalDefinition existing)
    {
      return existing;
    }

    var opts = new ExternalDefinitionCreationOptions(paramName, SpecTypeId.String.Text)
    {
      UserModifiable = true,
      Visible = true,
    };

    return group.Definitions.Create(opts) as ExternalDefinition;
  }

  /// <summary>
  /// Matched by label because that is what the send side publishes as the path's group segment
  /// (<c>LabelUtils.GetLabelForGroup</c>), so a path read back from a localized Revit resolves in that Revit.
  /// </summary>
  private ForgeTypeId ResolveGroup(string? groupLabel)
  {
    if (string.IsNullOrWhiteSpace(groupLabel))
    {
      return GroupTypeId.Data;
    }

    var label = groupLabel!.Trim();
    foreach (var group in ParameterUtils.GetAllBuiltInGroups())
    {
      if (LabelUtils.GetLabelForGroup(group) == label)
      {
        return group;
      }
    }

    _logger.LogWarning("Unknown parameter group '{GroupLabel}', creating under Data", label);
    return GroupTypeId.Data;
  }

  private UpdateResult EnsureCategoryBinding(
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
