using System.IO;
using Autodesk.Revit.Creation;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using DB = Autodesk.Revit.DB;
using Document = Autodesk.Revit.DB.Document;
using SMesh = Speckle.Objects.Geometry.Mesh;

namespace Speckle.Connectors.Revit.HostApp;

#pragma warning disable CA1506
public sealed class RevitFamilyBaker : IDisposable
#pragma warning restore CA1506
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _cache;
  private readonly ILogger<RevitFamilyBaker> _logger;
  private readonly ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform> _transformConverter;
  private readonly RevitMeshBuilder _revitMeshBuilder;
  private readonly ITypedConverter<Base, List<GeometryObject>> _geometryConverter;
  private readonly RevitMaterialBaker _materialBaker;
  private string? _cachedTemplatePath;
  private readonly Dictionary<string, string> _bakedFamilyPaths = [];

  public RevitFamilyBaker(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToHostCacheSingleton cache,
    ILogger<RevitFamilyBaker> logger,
    ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform> transformConverter,
    RevitMeshBuilder revitMeshBuilder,
    ITypedConverter<Base, List<GeometryObject>> geometryConverter,
    RevitMaterialBaker materialBaker
  )
  {
    _converterSettings = converterSettings;
    _cache = cache;
    _logger = logger;
    _transformConverter = transformConverter;
    _revitMeshBuilder = revitMeshBuilder;
    _geometryConverter = geometryConverter;
    _materialBaker = materialBaker;
  }

#pragma warning disable CA1502
  public (List<ReceiveConversionResult> results, List<string> createdElementIds) BakeInstances(
#pragma warning restore CA1502
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    IReadOnlyDictionary<string, TraversalContext> speckleObjectLookup,
    IReadOnlyCollection<RenderMaterialProxy> materialProxies,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var document = _converterSettings.Current.Document;
    var results = new List<ReceiveConversionResult>();
    var createdElementIds = new List<string>();

    var consumedIds = new HashSet<string>();

    // 1. Build fast lookup maps
    Dictionary<string, RenderMaterial> objectToMaterialMap = new();
    Dictionary<string, ElementId> safeNameToProjectMatId = new();

    foreach (var proxy in materialProxies)
    {
      string matId = proxy.value.id.NotNullOrWhiteSpace();
      // Ensure the key precisely matches the string generated for the parameter names!
      string safeName = string.IsNullOrWhiteSpace(proxy.value.name) ? matId : proxy.value.name;

      foreach (var objId in proxy.objects)
      {
        objectToMaterialMap[objId] = proxy.value;
      }

      // Map the Safe Name directly to the Project Material ElementId using cache
      if (proxy.objects.Count > 0)
      {
        foreach (var objId in proxy.objects)
        {
          if (_cache.MaterialsByObjectId.TryGetValue(objId, out var projMatId))
          {
            safeNameToProjectMatId[safeName] = projMatId;
            break; // Stop looking once we find the project material for this proxy
          }
        }
      }
    }

    foreach (var (_, component) in instanceComponents)
    {
      if (component is InstanceDefinitionProxy definition)
      {
        foreach (var childId in definition.objects ?? Enumerable.Empty<string>())
        {
          consumedIds.Add(childId);
          if (speckleObjectLookup.TryGetValue(childId, out var childTc))
          {
            var childObj = childTc.Current;
            if (childObj.id != null)
            {
              consumedIds.Add(childObj.id);
            }

            if (childObj.applicationId != null)
            {
              consumedIds.Add(childObj.applicationId);
            }
          }
        }
      }
    }

    var sortedComponents = instanceComponents
      .OrderByDescending(x => x.component.maxDepth)
      .ThenBy(x => x.component is InstanceDefinitionProxy ? 0 : 1)
      .ToList();

    var count = 0;
    foreach (var (_, component) in sortedComponents)
    {
      onOperationProgressed.Report(new("Creating families", (double)++count / sortedComponents.Count));

      try
      {
        if (component is InstanceDefinitionProxy definitionProxy)
        {
          var result = CreateFamilyFromDefinition(
            document,
            definitionProxy,
            speckleObjectLookup,
            objectToMaterialMap,
            safeNameToProjectMatId
          );
          if (result.HasValue)
          {
            results.Add(
              new ReceiveConversionResult(Status.SUCCESS, definitionProxy, result.Value.family.Id.ToString(), "Family")
            );
          }
        }
        else if (component is InstanceProxy instanceProxy)
        {
          bool isConsumed =
            (instanceProxy.id != null && consumedIds.Contains(instanceProxy.id))
            || (instanceProxy.applicationId != null && consumedIds.Contains(instanceProxy.applicationId));

          if (isConsumed)
          {
            continue;
          }

          var instance = PlaceFamilyInstance(document, instanceProxy);
          if (instance != null)
          {
            createdElementIds.Add(instance.UniqueId);
            results.Add(
              new ReceiveConversionResult(Status.SUCCESS, instanceProxy, instance.UniqueId, "FamilyInstance")
            );
          }
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        string componentId = component switch
        {
          InstanceDefinitionProxy d => d.applicationId ?? d.id.NotNull(),
          InstanceProxy i => i.applicationId ?? i.id.NotNull(),
          _ => "unknown"
        };
        _logger.LogError(ex, "Failed to process instance component {ComponentId}", componentId);

        if (component is Base b)
        {
          results.Add(new ReceiveConversionResult(Status.ERROR, b, null, null, ex));
        }
      }
    }

    return (results, createdElementIds);
  }

  private (Family family, FamilySymbol symbol)? CreateFamilyFromDefinition(
    Document document,
    InstanceDefinitionProxy definitionProxy,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap,
    IReadOnlyDictionary<string, ElementId> safeNameToProjectMatId
  )
  {
    var definitionId = definitionProxy.applicationId ?? definitionProxy.id.NotNull();

    if (_cache.FamiliesByDefinitionId.TryGetValue(definitionId, out var existingFamily))
    {
      var existingSymbol = _cache.SymbolsByDefinitionId[definitionId];
      return (existingFamily, existingSymbol);
    }

    var familyName = GetFamilyName(definitionProxy);
    var family =
      FindFamilyByName(document, familyName)
      ?? CreateFamily(document, familyName, definitionProxy, objectLookup, materialMap);

    if (family == null)
    {
      _logger.LogWarning("Failed to create family for definition {DefinitionId}", definitionId);
      return null;
    }

    var symbolId = family.GetFamilySymbolIds().FirstOrDefault();
    if (symbolId == null || symbolId == ElementId.InvalidElementId)
    {
      return null;
    }

    if (document.GetElement(symbolId) is not FamilySymbol symbol)
    {
      return null;
    }

    if (!symbol.IsActive)
    {
      symbol.Activate();
      document.Regenerate();
    }

    AssignProjectMaterialsToFamily(document, symbol, safeNameToProjectMatId);

    _cache.FamiliesByDefinitionId[definitionId] = family;
    _cache.SymbolsByDefinitionId[definitionId] = symbol;

    return (family, symbol);
  }

  private void AssignProjectMaterialsToFamily(
    Document document,
    FamilySymbol symbol,
    IReadOnlyDictionary<string, ElementId> safeNameToProjectMatId
  )
  {
    Category? baseCategory = document.Settings.Categories.get_Item(BuiltInCategory.OST_GenericModel);

    // 1. application for free form elements via type parameters
    foreach (Parameter p in symbol.Parameters)
    {
      if (p.Definition.Name.StartsWith("Material_") && p.StorageType == StorageType.ElementId)
      {
        string safeName = p.Definition.Name["Material_".Length..];

        if (safeNameToProjectMatId.TryGetValue(safeName, out var projMatId))
        {
          if (!p.IsReadOnly)
          {
            p.Set(projMatId);
          }
        }
      }
    }

    // 2. application for direct shapes via subcategories
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
          if (projSubCat != null)
          {
            if (document.GetElement(projMatId) is Material projMat)
            {
              projSubCat.Material = projMat;
            }
          }
        }
      }
    }
  }

  private Family? CreateFamily(
    Document document,
    string familyName,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap
  )
  {
    var templatePath = GetFamilyTemplatePath(document);
    var famDoc = document.Application.NewFamilyDocument(templatePath);
    var tempPath = Path.Combine(Path.GetTempPath(), $"{familyName}.rfa");

    try
    {
      using (var t = new Transaction(famDoc, "Populate Family"))
      {
        t.Start();
        PopulateFamily(famDoc, definition, objectLookup, materialMap);
        SetFamilyWorkPlaneBased(famDoc, true);
        t.Commit();
      }

      var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
      famDoc.SaveAs(tempPath, saveOptions);
      famDoc.Close(false);

      var definitionId = definition.applicationId ?? definition.id.NotNull();
      _bakedFamilyPaths[definitionId] = tempPath;

      document.LoadFamily(tempPath, new FamilyLoadOptions(), out var loadedFamily);
      return loadedFamily;
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
    {
      _logger.LogError(ex, "Revit API error creating family {FamilyName}", familyName);
      famDoc.Close(false);
      CleanupTempFile(tempPath);
      throw;
    }
    catch (IOException ex)
    {
      _logger.LogError(ex, "IO error creating family {FamilyName}", familyName);
      famDoc.Close(false);
      CleanupTempFile(tempPath);
      throw;
    }
  }

  private void PopulateFamily(
    Document famDoc,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap
  )
  {
    if (definition.objects.Count == 0)
    {
      return;
    }

    var materialManager = new FamilyMaterialManager(_materialBaker, _logger);
    materialManager.SetupFamilyMaterials(famDoc, definition, objectLookup, materialMap);

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
        materialMap
      );
    }
  }

  private void ProcessObjectForFamily(
    Document famDoc,
    Base obj,
    Category? currentSubcategory,
    string familyName,
    string? extractedSubcategoryName = null,
    FamilyMaterialManager? materialManager = null,
    IReadOnlyDictionary<string, RenderMaterial>? materialMap = null
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
          ProcessObjectForFamily(famDoc, element, newSubcategory, familyName, null, materialManager, materialMap);
        }
      }
      else if (obj is InstanceProxy instanceProxy)
      {
        PlaceNestedInstance(famDoc, instanceProxy, materialManager);
      }
      else
      {
        BakeGeometry(famDoc, obj, newSubcategory, materialManager, materialMap);
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
    IReadOnlyDictionary<string, RenderMaterial>? materialMap
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
        var solids = geometries.OfType<Solid>().Where(s => s.Volume > 0).ToList();
        var nonSolids = geometries.Where(g => g is not Solid s || s.Volume <= 0).ToList();

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
          ElementId categoryId = new(BuiltInCategory.OST_GenericModel);
          if (
            materialManager != null
            && speckleMatId != null
            && materialManager.SubCategories.TryGetValue(speckleMatId, out var subCatId)
          )
          {
            categoryId = subCatId;
          }

          using var ds = DirectShape.CreateElement(famDoc, categoryId);
          ds.SetShape(nonSolids);
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
        BakeGeometry(famDoc, item, subcategory, materialManager, materialMap);
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
    var geomObject = _revitMeshBuilder.BuildFreeformElementGeometry(mesh);

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
      ElementId categoryId = new(BuiltInCategory.OST_GenericModel);

      if (
        materialManager != null
        && speckleMatId != null
        && materialManager.SubCategories.TryGetValue(speckleMatId, out var subCatId)
      )
      {
        categoryId = subCatId;
      }

      using var ds = DirectShape.CreateElement(famDoc, categoryId);
      ds.SetShape([revitMesh]);
    }
  }

  private void PlaceNestedInstance(Document famDoc, InstanceProxy instanceProxy, FamilyMaterialManager? materialManager)
  {
    var childDefinitionId = instanceProxy.definitionId;

    if (!_bakedFamilyPaths.TryGetValue(childDefinitionId, out var rfaPath) || !File.Exists(rfaPath))
    {
      return;
    }

    var familyName = Path.GetFileNameWithoutExtension(rfaPath);
    Family? childFamily = FindFamilyByName(famDoc, familyName) ?? LoadFamilyWrapper(famDoc, rfaPath);

    using var _ = childFamily;
    if (childFamily == null)
    {
      return;
    }

    var symbolId = childFamily.GetFamilySymbolIds().FirstOrDefault();
    if (symbolId == null)
    {
      return;
    }

    if (famDoc.GetElement(symbolId) is not FamilySymbol symbol)
    {
      return;
    }

    if (!symbol.IsActive)
    {
      symbol.Activate();
    }

    var revitTransform = _transformConverter.Convert((instanceProxy.transform, instanceProxy.units));

    XYZ origin = revitTransform.Origin;
    XYZ basisX = revitTransform.BasisX.Normalize();
    XYZ basisY = revitTransform.BasisY.Normalize();

    var plane = DB.Plane.CreateByOriginAndBasis(origin, basisX, basisY);
    using var sketchPlane = SketchPlane.Create(famDoc, plane);

    var creationData = new FamilyInstanceCreationData(
      location: origin,
      symbol: symbol,
      host: sketchPlane,
      level: null,
      structuralType: StructuralType.NonStructural
    );

    var ids = famDoc.FamilyCreate.NewFamilyInstances2([creationData]);

    if (ids.Count > 0)
    {
      var instanceId = ids.First();
      var mirrorState = GetMirrorState(instanceProxy.transform);
      ApplyMirroring(famDoc, instanceId, plane, mirrorState);

      // bubble up nested material parameters
      if (materialManager != null)
      {
        foreach (Parameter childParam in symbol.Parameters)
        {
          if (childParam.Definition.Name.StartsWith("Material_") && childParam.StorageType == StorageType.ElementId)
          {
            string paramName = childParam.Definition.Name;

            FamilyParameter? parentFamParam =
              famDoc.FamilyManager.get_Parameter(paramName)
              ?? famDoc.FamilyManager.AddParameter(
                paramName,
                GroupTypeId.Materials,
                SpecTypeId.Reference.Material,
                false // Ensure it is created as a TYPE parameter in the parent too!
              );

            if (famDoc.FamilyManager.CanElementParameterBeAssociated(childParam))
            {
              try
              {
                famDoc.FamilyManager.AssociateElementParameterToFamilyParameter(childParam, parentFamParam);
              }
              catch (Autodesk.Revit.Exceptions.ArgumentException ex)
              {
                _logger.LogWarning(ex, "Failed to associate material parameter {ParamName}", paramName);
              }
            }
          }
        }
      }
    }
  }

  private static Family? LoadFamilyWrapper(Document doc, string path)
  {
    doc.LoadFamily(path, new FamilyLoadOptions(), out var family);
    return family;
  }

  private FamilyInstance? PlaceFamilyInstance(Document document, InstanceProxy instanceProxy)
  {
    var definitionId = instanceProxy.definitionId;
    var revitTransform = _transformConverter.Convert((instanceProxy.transform, instanceProxy.units));

    if (!_cache.SymbolsByDefinitionId.TryGetValue(definitionId, out var symbol))
    {
      _logger.LogWarning("No family symbol found for definition {DefinitionId}.", definitionId);
      return null;
    }

    XYZ origin = revitTransform.Origin;
    XYZ basisX = revitTransform.BasisX.Normalize();
    XYZ basisY = revitTransform.BasisY.Normalize();

    var plane = DB.Plane.CreateByOriginAndBasis(origin, basisX, basisY);
    using var sketchPlane = SketchPlane.Create(document, plane);

    var creationData = new FamilyInstanceCreationData(
      location: origin,
      symbol: symbol,
      host: sketchPlane,
      level: null,
      structuralType: StructuralType.NonStructural
    );

    var ids = document.Create.NewFamilyInstances2([creationData]);
    if (ids.Count == 0)
    {
      return null;
    }

    if (document.GetElement(ids.First()) is not FamilyInstance instance)
    {
      return null;
    }

    document.Regenerate();
    var mirrorState = GetMirrorState(instanceProxy.transform);
    ApplyMirroring(document, instance.Id, plane, mirrorState);

    return instance;
  }

  private static void SetFamilyWorkPlaneBased(Document famDoc, bool enabled)
  {
    var workPlaneBasedParam = famDoc.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
    if (workPlaneBasedParam != null && !workPlaneBasedParam.IsReadOnly)
    {
      workPlaneBasedParam.Set(enabled ? 1 : 0);
    }
  }

  private string GetFamilyTemplatePath(Document document)
  {
    if (_cachedTemplatePath != null)
    {
      return _cachedTemplatePath;
    }

    var version = document.Application.VersionNumber;
    var isMetric = document.DisplayUnitSystem == DisplayUnit.METRIC;
    var templateName = isMetric ? "Metric Generic Model.rft" : "Generic Model.rft";
    var assemblyLocation = typeof(RevitFamilyBaker).Assembly.Location;
    var assemblyDir =
      Path.GetDirectoryName(assemblyLocation) ?? throw new ConversionException("Could not resolve assembly directory");

    var templatePath = Path.Combine(assemblyDir, "Resources", "Templates", version, templateName);

    if (!File.Exists(templatePath))
    {
      _logger.LogError("Revit Family Template missing. Searched path: {templatePath}", templatePath);
      throw new ConversionException($"Could not find required family template: {templateName}");
    }

    _cachedTemplatePath = templatePath;
    return templatePath;
  }

  private static string GetFamilyName(InstanceDefinitionProxy definitionProxy)
  {
    var baseName = definitionProxy.name;
    var invalidChars = Path.GetInvalidFileNameChars();
    return string.Concat(baseName.Select(c => invalidChars.Contains(c) ? '_' : c));
  }

  private static Family? FindFamilyByName(Document document, string familyName)
  {
    using var collector = new FilteredElementCollector(document);
    return collector.OfClass(typeof(Family)).OfType<Family>().FirstOrDefault(f => f.Name == familyName);
  }

  private static (bool X, bool Y, bool Z) GetMirrorState(Matrix4x4 matrix)
  {
    var det =
      matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32)
      - matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31)
      + matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);

    return det < 0 ? (true, false, false) : (false, false, false);
  }

  private void ApplyMirroring(
    Document document,
    ElementId elementId,
    DB.Plane plane,
    (bool X, bool Y, bool Z) mirrorState
  )
  {
    var mirrorOperations = new List<(string name, bool shouldMirror, DB.Plane mirrorPlane)>
    {
      ("YZ", mirrorState.X, DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.YVec, plane.Normal)),
      ("XZ", mirrorState.Y, DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.Normal)),
      ("XY", mirrorState.Z, DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.YVec))
    };

    foreach (var (name, shouldMirror, mirrorPlane) in mirrorOperations.Where(op => op.shouldMirror))
    {
      try
      {
        document.Regenerate();
        ElementTransformUtils.MirrorElements(document, [elementId], mirrorPlane, false);
      }
      catch (Autodesk.Revit.Exceptions.ApplicationException e)
      {
        _logger.LogWarning(e, "Failed to mirror element on {PlaneName} plane", name);
      }
      finally
      {
        mirrorPlane.Dispose();
      }
    }
  }

  private static void CleanupTempFile(string path)
  {
    try
    {
      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
  }

  public void Dispose()
  {
    foreach (var path in _bakedFamilyPaths.Values)
    {
      CleanupTempFile(path);
    }

    _bakedFamilyPaths.Clear();
  }

  private sealed class FamilyLoadOptions : IFamilyLoadOptions
  {
    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
      overwriteParameterValues = true;
      return true;
    }

    public bool OnSharedFamilyFound(
      Family sharedFamily,
      bool familyInUse,
      out FamilySource source,
      out bool overwriteParameterValues
    )
    {
      source = FamilySource.Family;
      overwriteParameterValues = true;
      return true;
    }
  }
}

/// <summary>
/// Helper class extracted to reduce coupling in RevitFamilyBaker.
/// Manages the resolution and assignment of materials, subcategories, and parameters within the Family Document context.
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
}
