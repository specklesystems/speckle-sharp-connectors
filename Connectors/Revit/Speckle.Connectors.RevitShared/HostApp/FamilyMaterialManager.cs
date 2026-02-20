using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Manages the resolution and assignment of materials, subcategories, and parameters
/// strictly within the context of a temporary Family Document.
/// </summary>
public class FamilyMaterialManager
{
  private readonly RevitMaterialBaker _materialBaker;
  private readonly ILogger _logger;

  public Dictionary<string, FamilyParameter> FamilyParameters { get; } = [];
  public Dictionary<string, ElementId> SubCategories { get; } = [];
  private Dictionary<string, ElementId> BakedMaterials { get; } = [];

  public FamilyMaterialManager(RevitMaterialBaker materialBaker, ILogger logger)
  {
    _materialBaker = materialBaker;
    _logger = logger;
  }

  public void SetupFamilyMaterials(
    Document famDoc,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap
  )
  {
    Category baseCategory = famDoc.OwnerFamily.FamilyCategory;

    foreach (var id in definition.objects)
    {
      if (!objectLookup.TryGetValue(id, out var tc))
      {
        continue;
      }

      var obj = tc.Current;
      string objectId = obj.applicationId ?? obj.id.NotNull();

      if (materialMap.TryGetValue(objectId, out var renderMat))
      {
        if (BakedMaterials.ContainsKey(renderMat.id.NotNullOrWhiteSpace()))
        {
          continue;
        }

        try
        {
          // 1. Bake the material locally
          ElementId famMatId = _materialBaker.BakeMaterial(renderMat, famDoc);
          BakedMaterials[renderMat.id] = famMatId;

          // 2. Setup Subcategory (for DirectShapes)
          string safeName = string.IsNullOrWhiteSpace(renderMat.name) ? renderMat.id : renderMat.name;
          string subCatName = $"Mat_{safeName}";
          subCatName = subCatName.Length > 50 ? subCatName[..50] : subCatName;

          if (baseCategory != null)
          {
            if (!baseCategory.SubCategories.Contains(subCatName))
            {
              Category subCat = famDoc.Settings.Categories.NewSubcategory(baseCategory, subCatName);
              subCat.Material = famDoc.GetElement(famMatId) as Material;
              SubCategories[renderMat.id] = subCat.Id;
            }
            else
            {
              SubCategories[renderMat.id] = baseCategory.SubCategories.get_Item(subCatName).Id;
            }
          }

          // 3. Setup Family Parameter (for FreeFormElements)
          string paramName = $"Material_{safeName}";
          FamilyParameter? existingParam = famDoc.FamilyManager.get_Parameter(paramName);
          if (existingParam == null)
          {
            FamilyParameter famParam = famDoc.FamilyManager.AddParameter(
              paramName,
              GroupTypeId.Materials,
              SpecTypeId.Reference.Material,
              false
            );
            FamilyParameters[renderMat.id] = famParam;
          }
          else
          {
            FamilyParameters[renderMat.id] = existingParam;
          }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogWarning(ex, "Failed to setup family material {MatName}", renderMat.name);
        }
      }
    }
  }

  public static void AssignProjectMaterialsToFamily(
    Document document,
    FamilySymbol symbol,
    IReadOnlyDictionary<string, ElementId> safeNameToProjectMatId
  )
  {
    Category? baseCategory = document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);

    foreach (Parameter p in symbol.Parameters)
    {
      if (p.Definition.Name.StartsWith("Material_") && p.StorageType == StorageType.ElementId)
      {
        string safeName = p.Definition.Name["Material_".Length..];

        if (safeNameToProjectMatId.TryGetValue(safeName, out var projMatId) && !p.IsReadOnly)
        {
          p.Set(projMatId);
        }
      }
    }

    if (baseCategory != null)
    {
      foreach (var kvp in safeNameToProjectMatId)
      {
        string safeName = kvp.Key;
        ElementId projMatId = kvp.Value;

        string subCatName = $"Mat_{safeName}";
        subCatName = subCatName.Length > 50 ? subCatName[..50] : subCatName;

        if (baseCategory.SubCategories.Contains(subCatName))
        {
          Category projSubCat = baseCategory.SubCategories.get_Item(subCatName);
          if (projSubCat != null && document.GetElement(projMatId) is Material projMat)
          {
            projSubCat.Material = projMat;
          }
        }
      }
    }
  }
}
