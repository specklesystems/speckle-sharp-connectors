using System.Globalization;
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
  private static readonly char[] s_invalidRevitChars =
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
    '`',
    '~',
  ];

  public Dictionary<string, FamilyParameter> FamilyParameters { get; } = [];
  public Dictionary<string, ElementId> SubCategories { get; } = [];
  private Dictionary<string, ElementId> BakedMaterials { get; } = [];

  public FamilyMaterialManager(RevitMaterialBaker materialBaker, ILogger logger)
  {
    _materialBaker = materialBaker;
    _logger = logger;
  }

  /// <summary>
  /// Sanitizes string to ensure it is valid for Revit Parameters and SubCategories.
  /// </summary>
  public static string GetSafeName(string rawName)
  {
    if (string.IsNullOrWhiteSpace(rawName))
    {
      return "Unnamed";
    }

    char[] buffer = rawName.ToCharArray();
    bool changed = false;

    for (int i = 0; i < buffer.Length; i++)
    {
      if (Array.IndexOf(s_invalidRevitChars, buffer[i]) >= 0)
      {
        buffer[i] = '_';
        changed = true;
      }
    }

    return changed ? new string(buffer) : rawName;
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
          string rawName = string.IsNullOrWhiteSpace(renderMat.name) ? renderMat.id : renderMat.name;
          string safeName = GetSafeName(rawName);

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

  // ENG-9101: bundle-native counterparts of the two methods above, keyed by MATERIAL node index instead of a
  // sanitized material name — the artifact bundle already gives us a stable int key, so no name-collision handling
  // is needed. "Material_a{node}" (not "Material_{node}") so it can never collide with a v1 safeName-derived param.
  public void SetupFamilyMaterialsFromArtifact(
    Document famDoc,
    IEnumerable<int> materialNodeKeys,
    IReadOnlyDictionary<int, RenderMaterial> materialsByNode
  )
  {
    foreach (var matNodeK in materialNodeKeys)
    {
      if (!materialsByNode.TryGetValue(matNodeK, out var renderMat))
      {
        continue;
      }
      string key = matNodeK.ToString(CultureInfo.InvariantCulture);
      if (BakedMaterials.ContainsKey(key))
      {
        continue;
      }

      try
      {
        ElementId famMatId = _materialBaker.BakeMaterial(renderMat, famDoc);
        BakedMaterials[key] = famMatId;

        string paramName = $"Material_a{key}";
        FamilyParameters[key] =
          famDoc.FamilyManager.get_Parameter(paramName)
          ?? famDoc.FamilyManager.AddParameter(paramName, GroupTypeId.Materials, SpecTypeId.Reference.Material, false);
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogWarning(ex, "Failed to setup family material for node {MaterialNodeKey}", matNodeK);
      }
    }
  }

  public static void AssignProjectMaterialsToFamilyFromArtifact(
    FamilySymbol symbol,
    IReadOnlyDictionary<int, ElementId> projectMaterialIdByNode
  )
  {
    foreach (Parameter p in symbol.Parameters)
    {
      if (
        p.Definition.Name.StartsWith("Material_a", StringComparison.Ordinal)
        && p.StorageType == StorageType.ElementId
        && !p.IsReadOnly
        && int.TryParse(
          p.Definition.Name["Material_a".Length..],
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out int matNodeK
        )
        && projectMaterialIdByNode.TryGetValue(matNodeK, out var projMatId)
      )
      {
        p.Set(projMatId);
      }
    }
  }

  public static void AssignProjectMaterialsToFamily(
    Document document,
    FamilySymbol symbol,
    IReadOnlyDictionary<string, ElementId> originalNameToProjectMatId
  )
  {
    Category? baseCategory = document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);

    // Create a local map with sanitized keys so it perfectly matches the safeNames applied in the Family
    var sanitizedMatMap = new Dictionary<string, ElementId>();
    foreach (var kvp in originalNameToProjectMatId)
    {
      sanitizedMatMap[GetSafeName(kvp.Key)] = kvp.Value;
    }

    foreach (Parameter p in symbol.Parameters)
    {
      if (p.Definition.Name.StartsWith("Material_") && p.StorageType == StorageType.ElementId)
      {
        string safeName = p.Definition.Name["Material_".Length..];

        if (sanitizedMatMap.TryGetValue(safeName, out var projMatId) && !p.IsReadOnly)
        {
          p.Set(projMatId);
        }
      }
    }

    if (baseCategory != null)
    {
      foreach (var kvp in sanitizedMatMap)
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
