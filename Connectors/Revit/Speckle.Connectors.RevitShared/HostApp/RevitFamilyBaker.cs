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
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using DB = Autodesk.Revit.DB;
using Document = Autodesk.Revit.DB.Document;

namespace Speckle.Connectors.Revit.HostApp;

public sealed class RevitFamilyBaker : IDisposable
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _cache;
  private readonly ILogger<RevitFamilyBaker> _logger;
  private readonly ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform> _transformConverter;
  private readonly RevitMaterialBaker _materialBaker;
  private readonly FamilyGeometryBaker _familyGeometryBaker;
  private readonly FamilyCategoryUtils _familyCategoryUtils;
  private readonly FamilyTransformUtils _familyTransformUtils;

  private string? _cachedTemplatePath;
  private readonly Dictionary<string, string> _bakedFamilyPaths = [];

  private readonly string _tempDirectory;
  private static readonly char[] s_invalidChars = Path.GetInvalidFileNameChars();

  public RevitFamilyBaker(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToHostCacheSingleton cache,
    ILogger<RevitFamilyBaker> logger,
    ITypedConverter<(Matrix4x4 matrix, string units), DB.Transform> transformConverter,
    RevitMaterialBaker materialBaker,
    FamilyGeometryBaker familyGeometryBaker,
    FamilyCategoryUtils familyCategoryUtils,
    FamilyTransformUtils familyTransformUtils
  )
  {
    _converterSettings = converterSettings;
    _cache = cache;
    _logger = logger;
    _transformConverter = transformConverter;
    _materialBaker = materialBaker;
    _familyGeometryBaker = familyGeometryBaker;
    _familyCategoryUtils = familyCategoryUtils;
    _familyTransformUtils = familyTransformUtils;
    _tempDirectory = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid().ToString("N")[..8]}");
    Directory.CreateDirectory(_tempDirectory);
  }

  public (List<ReceiveConversionResult> results, List<string> createdElementIds) BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    IReadOnlyDictionary<string, TraversalContext> speckleObjectLookup,
    IReadOnlyCollection<RenderMaterialProxy> materialProxies,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var document = _converterSettings.Current.Document;
    var results = new List<ReceiveConversionResult>();
    var createdElementIds = new List<string>();

    var (objectToMaterialMap, safeNameToProjectMatId) = BuildMaterialMaps(materialProxies);
    var consumedIds = BuildConsumedIdsSet(instanceComponents, speckleObjectLookup);
    var sortedComponents = SortComponentsForBaking(instanceComponents);

    var count = 0;
    foreach (var (_, component) in sortedComponents)
    {
      onOperationProgressed.Report(new("Creating families", (double)++count / sortedComponents.Count));

      try
      {
        if (component is InstanceDefinitionProxy definitionProxy)
        {
          var categoryString = _familyCategoryUtils.ExtractCategoryForDefinition(
            definitionProxy,
            instanceComponents,
            speckleObjectLookup
          );
          var result = CreateFamilyFromDefinition(
            document,
            definitionProxy,
            speckleObjectLookup,
            objectToMaterialMap,
            safeNameToProjectMatId,
            categoryString
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

            if (_familyTransformUtils.HasScaleOrSkew(instanceProxy.transform))
            {
              var warningEx = new SpeckleException(
                "Block instance placed with its original position and rotation, but the unsupported scale/skew was dropped"
              );
              results.Add(
                new ReceiveConversionResult(
                  Status.WARNING,
                  instanceProxy,
                  instance.UniqueId,
                  "FamilyInstance",
                  warningEx
                )
              );
            }
            else
            {
              results.Add(
                new ReceiveConversionResult(Status.SUCCESS, instanceProxy, instance.UniqueId, "FamilyInstance")
              );
            }
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

  private (
    Dictionary<string, RenderMaterial> objectToMaterialMap,
    Dictionary<string, ElementId> safeNameToProjectMatId
  ) BuildMaterialMaps(IReadOnlyCollection<RenderMaterialProxy> materialProxies)
  {
    Dictionary<string, RenderMaterial> objectToMaterialMap = new();
    Dictionary<string, ElementId> safeNameToProjectMatId = new();

    foreach (var proxy in materialProxies)
    {
      string matId = proxy.value.id.NotNullOrWhiteSpace();
      string safeName = string.IsNullOrWhiteSpace(proxy.value.name) ? matId : proxy.value.name;

      foreach (var objId in proxy.objects)
      {
        objectToMaterialMap[objId] = proxy.value;
      }

      if (proxy.objects.Count > 0)
      {
        foreach (var objId in proxy.objects)
        {
          if (_cache.MaterialsByObjectId.TryGetValue(objId, out var projMatId))
          {
            safeNameToProjectMatId[safeName] = projMatId;
            break;
          }
        }
      }
    }

    return (objectToMaterialMap, safeNameToProjectMatId);
  }

  private static HashSet<string> BuildConsumedIdsSet(
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    IReadOnlyDictionary<string, TraversalContext> speckleObjectLookup
  )
  {
    var consumedIds = new HashSet<string>();

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

    return consumedIds;
  }

  private static List<(Collection[] collectionPath, IInstanceComponent component)> SortComponentsForBaking(
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents
  ) =>
    instanceComponents
      .OrderByDescending(x => x.component.maxDepth)
      .ThenBy(x => x.component is InstanceDefinitionProxy ? 0 : 1)
      .ToList();

  private (Family family, FamilySymbol symbol)? CreateFamilyFromDefinition(
    Document document,
    InstanceDefinitionProxy definitionProxy,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap,
    IReadOnlyDictionary<string, ElementId> safeNameToProjectMatId,
    string? categoryString
  )
  {
    var definitionId = definitionProxy.applicationId ?? definitionProxy.id.NotNull();

    if (_cache.FamiliesByDefinitionId.TryGetValue(definitionId, out var existingFamily))
    {
      var existingSymbol = _cache.SymbolsByDefinitionId[definitionId];
      return (existingFamily, existingSymbol);
    }

    var familyName = GetFamilyName(definitionProxy);

    bool isNewFamily = false;
    var family = FindFamilyByName(document, familyName);

    if (family == null)
    {
      family = CreateFamily(document, familyName, definitionProxy, objectLookup, materialMap, categoryString);
      isNewFamily = true;
    }

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

    if (isNewFamily)
    {
      FamilyMaterialManager.AssignProjectMaterialsToFamily(document, symbol, safeNameToProjectMatId);
    }

    _cache.FamiliesByDefinitionId[definitionId] = family;
    _cache.SymbolsByDefinitionId[definitionId] = symbol;

    return (family, symbol);
  }

  private Family? CreateFamily(
    Document document,
    string familyName,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, TraversalContext> objectLookup,
    IReadOnlyDictionary<string, RenderMaterial> materialMap,
    string? categoryString
  )
  {
    var templatePath = GetFamilyTemplatePath(document);
    var famDoc = document.Application.NewFamilyDocument(templatePath);
    var tempPath = Path.Combine(_tempDirectory, $"{familyName}.rfa");

    try
    {
      using (var t = new Transaction(famDoc, "Populate Family"))
      {
        t.Start();

        var materialManager = new FamilyMaterialManager(_materialBaker, _logger);
        materialManager.SetupFamilyMaterials(famDoc, definition, objectLookup, materialMap);

        _familyGeometryBaker.BakeFamilyGeometry(
          famDoc,
          definition,
          objectLookup,
          materialMap,
          materialManager,
          PlaceNestedInstance
        );

        SetFamilyWorkPlaneBased(famDoc, true);
        _familyCategoryUtils.SetFamilyCategory(famDoc, categoryString);
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
      SafeDelete(tempPath);
      throw;
    }
    catch (IOException ex)
    {
      _logger.LogError(ex, "IO error creating family {FamilyName}", familyName);
      famDoc.Close(false);
      SafeDelete(tempPath);
      throw;
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
    if (symbolId == null || famDoc.GetElement(symbolId) is not FamilySymbol symbol)
    {
      return;
    }

    if (!symbol.IsActive)
    {
      symbol.Activate();
    }

    var instance = CreateAndPlaceFamilyInstance(famDoc, instanceProxy, symbol);

    if (instance != null && materialManager != null)
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
              false
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

  private static Family? LoadFamilyWrapper(Document doc, string path)
  {
    doc.LoadFamily(path, new FamilyLoadOptions(), out var family);
    return family;
  }

  private FamilyInstance? CreateAndPlaceFamilyInstance(Document doc, InstanceProxy instanceProxy, FamilySymbol symbol)
  {
    var isMirrored = _familyTransformUtils.GetMirrorState(instanceProxy.transform).X;
    var hasScaleOrSkew = _familyTransformUtils.HasScaleOrSkew(instanceProxy.transform);

    var cleanMatrix =
      (hasScaleOrSkew || isMirrored)
        ? _familyTransformUtils.RemoveScaleAndSkew(instanceProxy.transform)
        : instanceProxy.transform;

    var revitTransform = _transformConverter.Convert((cleanMatrix, instanceProxy.units));

    XYZ origin = revitTransform.Origin;
    XYZ basisX = revitTransform.BasisX.Normalize();
    XYZ basisY = revitTransform.BasisY.Normalize();

    var plane = DB.Plane.CreateByOriginAndBasis(origin, basisX, basisY);
    using var sketchPlane = SketchPlane.Create(doc, plane);

    var creationData = new FamilyInstanceCreationData(
      location: origin,
      symbol: symbol,
      host: sketchPlane,
      level: null,
      structuralType: StructuralType.NonStructural
    );

    ICollection<ElementId> ids = doc.IsFamilyDocument
      ? doc.FamilyCreate.NewFamilyInstances2([creationData])
      : doc.Create.NewFamilyInstances2([creationData]);

    if (ids.Count == 0 || doc.GetElement(ids.First()) is not FamilyInstance instance)
    {
      return null;
    }

    doc.Regenerate();
    var mirrorState = _familyTransformUtils.GetMirrorState(instanceProxy.transform);
    _familyTransformUtils.ApplyMirroring(doc, instance.Id, plane, mirrorState);

    return instance;
  }

  private FamilyInstance? PlaceFamilyInstance(Document document, InstanceProxy instanceProxy)
  {
    var definitionId = instanceProxy.definitionId;

    if (_cache.SymbolsByDefinitionId.TryGetValue(definitionId, out var symbol))
    {
      return CreateAndPlaceFamilyInstance(document, instanceProxy, symbol);
    }

    _logger.LogWarning("No family symbol found for definition {DefinitionId}", definitionId);
    return null;
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
    if (string.IsNullOrWhiteSpace(baseName))
    {
      return "Unnamed_Block";
    }

    char[] buffer = baseName.ToCharArray();
    bool changed = false;

    for (int i = 0; i < buffer.Length; i++)
    {
      if (Array.IndexOf(s_invalidChars, buffer[i]) >= 0)
      {
        buffer[i] = '_';
        changed = true;
      }
    }

    var safeName = changed ? new string(buffer) : baseName;

    // truncate to avoid MAX_PATH exceptions. 100 chars should be very safe.
    if (safeName.Length > 100)
    {
      // Append a short hash of the definition ID to guarantee uniqueness after truncation
      var shortId = definitionProxy.id?[..8] ?? Guid.NewGuid().ToString("N")[..8];
      return $"{safeName[..90]}_{shortId}";
    }

    return safeName;
  }

  private static Family? FindFamilyByName(Document document, string familyName)
  {
    using var collector = new FilteredElementCollector(document);
    return collector.OfClass(typeof(Family)).OfType<Family>().FirstOrDefault(f => f.Name == familyName);
  }

  private static void SafeDelete(string path)
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
    _bakedFamilyPaths.Clear();

    try
    {
      if (Directory.Exists(_tempDirectory))
      {
        Directory.Delete(_tempDirectory, true);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
      _logger.LogWarning(ex, "Failed to clean up temporary family directory at {TempDir}", _tempDirectory);
    }
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
