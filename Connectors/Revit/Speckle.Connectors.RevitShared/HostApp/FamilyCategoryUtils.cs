using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Objects.Data;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.HostApp;

public static class FamilyCategoryUtils
{
  public static string? ExtractCategoryForDefinition(
    InstanceDefinitionProxy definition,
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    IReadOnlyDictionary<string, TraversalContext> speckleObjectLookup
  )
  {
    var definitionId = definition.applicationId ?? definition.id;

    var firstInstance = instanceComponents
      .Select(x => x.component)
      .OfType<InstanceProxy>()
      .FirstOrDefault(i => i.definitionId == definitionId);

    if (firstInstance == null)
    {
      return null;
    }

    var instanceObjectId = firstInstance.applicationId ?? firstInstance.id;
    if (instanceObjectId != null && speckleObjectLookup.TryGetValue(instanceObjectId, out var tc))
    {
      var parentDataObject = tc.Parent?.Current as DataObject;
      return CategoryExtractor.ExtractBuiltInCategory(parentDataObject, tc.Current);
    }

    return null;
  }

  public static void SetFamilyCategory(Document familyDoc, string? builtInCategoryString, ILogger logger)
  {
    if (!familyDoc.IsFamilyDocument || string.IsNullOrEmpty(builtInCategoryString))
    {
      return;
    }

    if (Enum.TryParse(builtInCategoryString, out BuiltInCategory bic))
    {
      try
      {
        Category targetCategory = familyDoc.Settings.Categories.get_Item(bic);
        if (targetCategory != null)
        {
          familyDoc.OwnerFamily.FamilyCategory = targetCategory;
        }
      }
      catch (Autodesk.Revit.Exceptions.ArgumentException)
      {
        logger.LogInformation("Category {Category} cannot be assigned to a Family. Falling back to default.", bic);
      }
      catch (Autodesk.Revit.Exceptions.InvalidOperationException ex)
      {
        logger.LogWarning(ex, "Invalid operation when setting category {Category}. Falling back to default.", bic);
      }
    }
  }
}
