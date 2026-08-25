using System.Globalization;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using SMesh = Speckle.Objects.Geometry.Mesh;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Handles the low-level conversion and baking of Speckle geometries into a Revit Family Document.
/// Extracted from RevitFamilyBaker to separate geometry generation from high-level orchestration, reducing class coupling.
/// </summary>
public class FamilyGeometryBaker
{
  private readonly ILogger<FamilyGeometryBaker> _logger;
  private readonly ITypedConverter<Base, List<GeometryObject>> _geometryConverter;
  private readonly ITypedConverter<SMesh, GeometryObject> _freeformMeshConverter;

  public FamilyGeometryBaker(
    ILogger<FamilyGeometryBaker> logger,
    ITypedConverter<Base, List<GeometryObject>> geometryConverter,
    ITypedConverter<SMesh, GeometryObject> freeformMeshConverter
  )
  {
    _logger = logger;
    _geometryConverter = geometryConverter;
    _freeformMeshConverter = freeformMeshConverter;
  }

  public void BakeFamilyGeometry(
    Document famDoc,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap,
    FamilyMaterialManager materialManager,
    Action<Document, InstanceProxy, FamilyMaterialManager?> placeNestedInstanceAction
  )
  {
    if (definition.objects.Count == 0)
    {
      return;
    }

    foreach (var id in definition.objects)
    {
      if (!objectLookup.TryGetValue(id, out var tc))
      {
        continue;
      }

      string? extractedSubcategoryName = null;
      var parentTc = tc.Parent;
      while (parentTc != null)
      {
        if (parentTc.Current is Collection col && !string.IsNullOrWhiteSpace(col.name))
        {
          extractedSubcategoryName = col.name;
          break;
        }
        parentTc = parentTc.Parent;
      }

      ProcessObjectForFamily(
        famDoc,
        tc.Current,
        null,
        definition.name,
        extractedSubcategoryName,
        materialManager,
        materialMap,
        placeNestedInstanceAction
      );
    }
  }

  private void ProcessObjectForFamily(
    Document famDoc,
    Base obj,
    Category? currentSubcategory,
    string familyName,
    string? extractedSubcategoryName,
    FamilyMaterialManager? materialManager,
    IReadOnlyDictionary<string, RenderMaterial>? materialMap,
    Action<Document, InstanceProxy, FamilyMaterialManager?> placeNestedInstanceAction
  )
  {
    try
    {
      Category? newSubcategory = currentSubcategory;
      string? subcategoryName = extractedSubcategoryName ?? (obj as Collection)?.name;

      if (!string.IsNullOrWhiteSpace(subcategoryName))
      {
        var familyCategory = famDoc.OwnerFamily.FamilyCategory;
        if (familyCategory != null)
        {
          if (familyCategory.SubCategories.Contains(subcategoryName))
          {
            newSubcategory = familyCategory.SubCategories.get_Item(subcategoryName);
          }
          else
          {
            try
            {
              newSubcategory = famDoc.Settings.Categories.NewSubcategory(familyCategory, subcategoryName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
              _logger.LogWarning("Failed to create Revit subcategory with name: {SubcategoryName}", subcategoryName);
              newSubcategory = currentSubcategory;
            }
          }
        }
      }

      if (obj is Collection col)
      {
        foreach (var element in col.elements)
        {
          ProcessObjectForFamily(
            famDoc,
            element,
            newSubcategory,
            familyName,
            null,
            materialManager,
            materialMap,
            placeNestedInstanceAction
          );
        }
      }
      else if (obj is InstanceProxy instanceProxy)
      {
        placeNestedInstanceAction(famDoc, instanceProxy, materialManager);
      }
      else
      {
        BakeGeometry(famDoc, obj, newSubcategory, materialManager, materialMap, placeNestedInstanceAction);
      }
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
    {
      _logger.LogWarning(ex, "Revit API error baking object {ObjectId} into family {Family}", obj.id, familyName);
    }
    catch (SpeckleException ex)
    {
      _logger.LogWarning(ex, "Speckle error baking object {ObjectId} into family {Family}", obj.id, familyName);
    }
  }

  private void BakeGeometry(
    Document famDoc,
    Base obj,
    Category? subcategory,
    FamilyMaterialManager? materialManager,
    IReadOnlyDictionary<string, RenderMaterial>? materialMap,
    Action<Document, InstanceProxy, FamilyMaterialManager?> placeNestedInstanceAction
  )
  {
    string objectId = obj.applicationId ?? obj.id.NotNull();
    string? speckleMatId = null;

    if (materialMap != null && materialMap.TryGetValue(objectId, out var mat))
    {
      speckleMatId = mat.id;
    }

    if (obj is SMesh mesh)
    {
      BakeMesh(famDoc, mesh, subcategory, speckleMatId, materialManager);
      return;
    }

    try
    {
      var geometries = _geometryConverter.Convert(obj);

      if (geometries.Count > 0)
      {
        var solids = new List<Solid>(geometries.Count);
        var nonSolids = new List<GeometryObject>(geometries.Count);

        foreach (var geom in geometries)
        {
          if (geom is Solid s && !s.Faces.IsEmpty)
          {
            solids.Add(s);
          }
          else
          {
            nonSolids.Add(geom);
          }
        }

        foreach (var solid in solids)
        {
          using var freeFormElement = FreeFormElement.Create(famDoc, solid);
          if (subcategory != null)
          {
            freeFormElement.Subcategory = subcategory;
          }

          if (
            materialManager != null
            && speckleMatId != null
            && materialManager.FamilyParameters.TryGetValue(speckleMatId, out var famParam)
          )
          {
            Parameter ffeMatParam = freeFormElement.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (ffeMatParam != null && famDoc.FamilyManager.CanElementParameterBeAssociated(ffeMatParam))
            {
              famDoc.FamilyManager.AssociateElementParameterToFamilyParameter(ffeMatParam, famParam);
            }
          }
        }

        if (nonSolids.Count > 0)
        {
          // try to use the Family's actual Category, otherwise default to Generic Model
          ElementId categoryId =
            famDoc.OwnerFamily.FamilyCategory?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);

          if (!DirectShape.IsValidCategoryId(categoryId, famDoc))
          {
            categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
          }

          try
          {
            using var ds = DirectShape.CreateElement(famDoc, categoryId);
            ds.SetShape(nonSolids);
          }
          catch (Autodesk.Revit.Exceptions.ArgumentException ex)
          {
            _logger.LogWarning(
              ex,
              "DirectShape rejected Category ID {CategoryId}, falling back to Generic Model",
              categoryId
            );

            using var fallbackDs = DirectShape.CreateElement(famDoc, new ElementId(BuiltInCategory.OST_GenericModel));
            fallbackDs.SetShape(nonSolids);
          }
        }

        return;
      }
    }
    catch (SpeckleException) { }

    var displayValues = obj.TryGetDisplayValue();
    if (displayValues != null)
    {
      foreach (var item in displayValues)
      {
        BakeGeometry(famDoc, item, subcategory, materialManager, materialMap, placeNestedInstanceAction);
      }
    }
  }

  // Looks a subcategory up on the family category and creates it when absent — the same lookup-or-create v1's
  // ProcessObjectForFamily did. Revit rejects names containing \ : { } [ ] | ; < > ? ` * ~, so the catch is
  // load-bearing: an unusable layer name logs and bakes without a subcategory rather than failing the definition.
  private Category? GetOrCreateSubcategory(Document famDoc, string? name, Dictionary<string, Category?> cache)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return null;
    }
    if (cache.TryGetValue(name!, out var cached))
    {
      return cached;
    }

    Category? resolved = null;
    var familyCategory = famDoc.OwnerFamily.FamilyCategory;
    if (familyCategory != null)
    {
      if (familyCategory.SubCategories.Contains(name))
      {
        resolved = familyCategory.SubCategories.get_Item(name);
      }
      else
      {
        try
        {
          resolved = famDoc.Settings.Categories.NewSubcategory(familyCategory, name);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
          _logger.LogWarning("Failed to create Revit subcategory with name: {SubcategoryName}", name);
        }
      }
    }
    cache[name!] = resolved;
    return resolved;
  }

  // ENG-9101: bundle-native counterpart of BakeFamilyGeometry above — bakes already-decoded GeometryObjects (the
  // caller resolved these from the artifact bundle via the same DecodeGeometry path DirectShape receive uses)
  // instead of walking a Base/TraversalContext graph. Members carry their source layer as a subcategory name, which
  // the caller recovered through the definition-member join [ENG-9343]; materials apply via family parameters.
  public void BakeFamilyGeometryFromArtifact(
    Document famDoc,
    IReadOnlyList<(GeometryObject geometry, int? materialNodeKey, string? subcategoryName)> members,
    FamilyMaterialManager materialManager
  )
  {
    // NewSubcategory throws on a name that already exists, and Contains on every member of a large definition is not
    // free — so resolve each name once per family document.
    var subcategoryCache = new Dictionary<string, Category?>(StringComparer.Ordinal);
    foreach (var (geometry, materialNodeKey, subcategoryName) in members)
    {
      try
      {
        if (geometry is Solid solid && !solid.Faces.IsEmpty)
        {
          using var freeFormElement = FreeFormElement.Create(famDoc, solid);
          // Solid/FreeFormElement only — the DirectShape fallback below never carried a subcategory in v1 either.
          if (GetOrCreateSubcategory(famDoc, subcategoryName, subcategoryCache) is { } subcategory)
          {
            freeFormElement.Subcategory = subcategory;
          }
          if (
            materialNodeKey is int mk
            && materialManager.FamilyParameters.TryGetValue(mk.ToString(CultureInfo.InvariantCulture), out var famParam)
          )
          {
            Parameter ffeMatParam = freeFormElement.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
            if (ffeMatParam != null && famDoc.FamilyManager.CanElementParameterBeAssociated(ffeMatParam))
            {
              famDoc.FamilyManager.AssociateElementParameterToFamilyParameter(ffeMatParam, famParam);
            }
          }
          continue;
        }

        ElementId categoryId = famDoc.OwnerFamily.FamilyCategory?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);
        if (!DirectShape.IsValidCategoryId(categoryId, famDoc))
        {
          categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
        }
        try
        {
          using var ds = DirectShape.CreateElement(famDoc, categoryId);
          ds.SetShape([geometry]);
        }
        catch (Autodesk.Revit.Exceptions.ArgumentException)
        {
          using var fallbackDs = DirectShape.CreateElement(famDoc, new ElementId(BuiltInCategory.OST_GenericModel));
          fallbackDs.SetShape([geometry]);
        }
      }
      catch (Autodesk.Revit.Exceptions.ApplicationException ex)
      {
        _logger.LogWarning(ex, "Revit API error baking artifact geometry into family");
      }
    }
  }

  private void BakeMesh(
    Document famDoc,
    SMesh mesh,
    Category? subcategory,
    string? speckleMatId,
    FamilyMaterialManager? materialManager
  )
  {
    GeometryObject geomObject;
    try
    {
      geomObject = _freeformMeshConverter.Convert(mesh);
    }
    catch (SpeckleException ex)
    {
      _logger.LogWarning(ex, "Failed to convert Speckle Mesh into Revit freeform geometry.");
      return;
    }

    if (geomObject is Solid solid)
    {
      using var freeFormElement = FreeFormElement.Create(famDoc, solid);
      if (subcategory != null)
      {
        freeFormElement.Subcategory = subcategory;
      }

      if (
        materialManager != null
        && speckleMatId != null
        && materialManager.FamilyParameters.TryGetValue(speckleMatId, out var famParam)
      )
      {
        Parameter ffeMatParam = freeFormElement.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
        if (ffeMatParam != null && famDoc.FamilyManager.CanElementParameterBeAssociated(ffeMatParam))
        {
          famDoc.FamilyManager.AssociateElementParameterToFamilyParameter(ffeMatParam, famParam);
        }
      }
    }
    else if (geomObject is Mesh revitMesh)
    {
      // try to use the Family's actual Category, otherwise default to Generic Model
      ElementId categoryId = famDoc.OwnerFamily.FamilyCategory?.Id ?? new ElementId(BuiltInCategory.OST_GenericModel);

      if (!DirectShape.IsValidCategoryId(categoryId, famDoc))
      {
        categoryId = new ElementId(BuiltInCategory.OST_GenericModel);
      }

      try
      {
        using var ds = DirectShape.CreateElement(famDoc, categoryId);
        ds.SetShape([revitMesh]);
      }
      catch (Autodesk.Revit.Exceptions.ArgumentException ex)
      {
        _logger.LogWarning(
          ex,
          "DirectShape rejected Category ID {CategoryId}, falling back to Generic Model",
          categoryId
        );

        using var fallbackDs = DirectShape.CreateElement(famDoc, new ElementId(BuiltInCategory.OST_GenericModel));
        fallbackDs.SetShape([revitMesh]);
      }
    }
  }
}
