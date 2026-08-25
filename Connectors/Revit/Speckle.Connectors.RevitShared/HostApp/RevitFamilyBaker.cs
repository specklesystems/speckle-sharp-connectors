using System.IO;
using Autodesk.Revit.Creation;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
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
using Speckle.Sdk.Pipelines.Progress;
using Speckle.Sdk.Pipelines.Receive.Artifacts;
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

  // referencePointTransform [ENG-9099]: the composed (receiver setting ∘ sender's recorded) reference-point
  // transform, or null when neither is set. Applied ONLY to the outermost placement of a top-level InstanceProxy
  // (via PlaceFamilyInstance below) — every instance's own placement transform is sent already expressed in
  // world/shared-coordinate space (same as atomic geometry), so it needs this correction to land back in the
  // receiving document's internal coordinates. Nested placements baked while AUTHORING a family template
  // (PlaceNestedInstance, inside a temporary famDoc) stay purely local/relative to that family and must NOT get
  // this correction — passing null there is deliberate, not an oversight.
  public (List<ReceiveConversionResult> results, List<string> createdElementIds) BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    IReadOnlyDictionary<string, TraversalContext> speckleObjectLookup,
    IReadOnlyCollection<RenderMaterialProxy> materialProxies,
    IProgress<CardProgress> onOperationProgressed,
    DB.Transform? referencePointTransform
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

          var instance = PlaceFamilyInstance(document, instanceProxy, referencePointTransform);

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
          _ => "unknown",
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

  /// <summary>
  /// Each definition member's source layer, keyed by the member's geometry K, for use as a Revit subcategory name
  /// [ENG-9343]. A member carries no <c>DISPLAY</c> edge so <c>ObjectByGeometry()</c> cannot reach its object row —
  /// and its layer lives on that row — so this goes through the definition-member join [ENG-9110], the same one
  /// Rhino receive uses. The LEAF container name, not the full path, matching v1's "nearest named Collection".
  /// </summary>
  public Dictionary<int, string> BuildMemberSubcategoryNames(ArtefactBundle bundle, ArtefactRelations rels)
  {
    var names = new Dictionary<int, string>();
    var memberIndex = DefinitionMemberIndexes.Build(rels, bundle.Properties);
    foreach (var kv in memberIndex.ObjectByGeometry)
    {
      var segments = SceneViewResolver.Segments(bundle, kv.Value);
      if (segments.Count > 0 && segments[^1] is { Length: > 0 } leaf)
      {
        names[kv.Key] = leaf;
      }
    }
    return names;
  }

  /// <summary>
  /// Bundle-native counterpart of <see cref="CreateFamilyFromDefinition"/> (ENG-9101): builds/reuses a real Revit
  /// family straight from already-decoded artifact geometry — no Base graph, no <see cref="TraversalContext"/>.
  /// </summary>
  /// <remarks>Deliberately does NOT use <see cref="_cache"/> (a DI singleton, so it outlives one receive): its keys
  /// are stable source applicationIds in the v1 pipeline, but <paramref name="definitionKey"/> here is a bundle
  /// position index that can be reassigned to a different definition on the next send. <see cref="FindFamilyByName"/>
  /// (stable — Revit family identity) is what makes reuse across receives safe; the caller's own per-receive
  /// dictionary already memoizes within one receive.</remarks>
  public (Family family, FamilySymbol symbol)? BakeDefinitionFromArtifact(
    Document document,
    string definitionKey,
    string? definitionName,
    string? categoryString,
    IReadOnlyList<(GeometryObject geometry, int? materialNodeKey, string? subcategoryName)> members,
    IReadOnlyDictionary<int, RenderMaterial> familyMaterialsByNode,
    IReadOnlyDictionary<int, ElementId> projectMaterialIdByNode,
    IReadOnlyList<(string childDefinitionKey, Matrix4x4 transform, string units)> nestedPlacements
  )
  {
    var familyName = GetFamilyName(definitionName, definitionKey);

    var family = FindFamilyByName(document, familyName);
    bool isNewFamily = family == null;
    if (family == null)
    {
      family = CreateFamilyFromArtifact(
        document,
        familyName,
        definitionKey,
        categoryString,
        members,
        familyMaterialsByNode,
        nestedPlacements
      );
    }

    if (family == null)
    {
      _logger.LogWarning("Failed to create family for artifact definition {DefinitionKey}", definitionKey);
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
      FamilyMaterialManager.AssignProjectMaterialsToFamilyFromArtifact(symbol, projectMaterialIdByNode);
    }

    return (family, symbol);
  }

  private Family? CreateFamilyFromArtifact(
    Document document,
    string familyName,
    string definitionKey,
    string? categoryString,
    IReadOnlyList<(GeometryObject geometry, int? materialNodeKey, string? subcategoryName)> members,
    IReadOnlyDictionary<int, RenderMaterial> familyMaterialsByNode,
    IReadOnlyList<(string childDefinitionKey, Matrix4x4 transform, string units)> nestedPlacements
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
        var usedMaterialNodeKeys = members
          .Where(m => m.materialNodeKey is not null)
          .Select(m => m.materialNodeKey!.Value)
          .Distinct();
        materialManager.SetupFamilyMaterialsFromArtifact(famDoc, usedMaterialNodeKeys, familyMaterialsByNode);

        _familyGeometryBaker.BakeFamilyGeometryFromArtifact(famDoc, members, materialManager);

        foreach (var (childDefinitionKey, transform, units) in nestedPlacements)
        {
          PlaceNestedInstanceFromArtifact(famDoc, childDefinitionKey, transform, units);
        }

        SetFamilyWorkPlaneBased(famDoc, true);
        _familyCategoryUtils.SetFamilyCategory(famDoc, categoryString);
        t.Commit();
      }

      var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
      famDoc.SaveAs(tempPath, saveOptions);
      famDoc.Close(false);

      _bakedFamilyPaths[definitionKey] = tempPath;

      document.LoadFamily(tempPath, new FamilyLoadOptions(), out var loadedFamily);
      return loadedFamily;
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException ex)
    {
      _logger.LogError(ex, "Revit API error creating artifact family {FamilyName}", familyName);
      famDoc.Close(false);
      SafeDelete(tempPath);
      throw;
    }
    catch (IOException ex)
    {
      _logger.LogError(ex, "IO error creating artifact family {FamilyName}", familyName);
      famDoc.Close(false);
      SafeDelete(tempPath);
      throw;
    }
  }

  // Bundle-native counterpart of PlaceNestedInstance: the child definition was already baked (depth-first) by the
  // caller, so we only need its saved .rfa path — loaded a second time here because a family document can't
  // reference a symbol living in a different document. Doesn't propagate the child's material params onto the
  // parent (a secondary nicety for swapping a nested block's materials from the parent) — the child still renders
  // with whatever materials it baked internally.
  private void PlaceNestedInstanceFromArtifact(
    Document famDoc,
    string childDefinitionKey,
    Matrix4x4 transform,
    string units
  )
  {
    if (!_bakedFamilyPaths.TryGetValue(childDefinitionKey, out var rfaPath) || !File.Exists(rfaPath))
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

    CreateAndPlaceFamilyInstance(famDoc, transform, units, symbol, referencePointTransform: null);
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

    var instance = CreateAndPlaceFamilyInstance(famDoc, instanceProxy, symbol, referencePointTransform: null);

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

  private FamilyInstance? CreateAndPlaceFamilyInstance(
    Document doc,
    InstanceProxy instanceProxy,
    FamilySymbol symbol,
    DB.Transform? referencePointTransform
  ) => CreateAndPlaceFamilyInstance(doc, instanceProxy.transform, instanceProxy.units, symbol, referencePointTransform);

  // Matrix/units core shared by the Base-graph (InstanceProxy) and bundle-native placement call sites.
  private FamilyInstance? CreateAndPlaceFamilyInstance(
    Document doc,
    Matrix4x4 transform,
    string units,
    FamilySymbol symbol,
    DB.Transform? referencePointTransform
  )
  {
    var isMirrored = _familyTransformUtils.GetMirrorState(transform).X;
    var hasScaleOrSkew = _familyTransformUtils.HasScaleOrSkew(transform);

    var cleanMatrix = (hasScaleOrSkew || isMirrored) ? _familyTransformUtils.RemoveScaleAndSkew(transform) : transform;

    var revitTransform = _transformConverter.Convert((cleanMatrix, units));
    if (referencePointTransform is not null)
    {
      // the placement transform is sent in world/shared-coordinate space (same frame as atomic geometry) — compose
      // the reference-point transform onto this OUTERMOST placement to land back in the document's internal
      // coordinates, mirroring RevitHostObjectArtefactBuilder.BuildInstanceTransform [ENG-9099].
      revitTransform = referencePointTransform.Multiply(revitTransform);
    }

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
    var mirrorState = _familyTransformUtils.GetMirrorState(transform);
    _familyTransformUtils.ApplyMirroring(doc, instance.Id, plane, mirrorState);

    return instance;
  }

  private FamilyInstance? PlaceFamilyInstance(
    Document document,
    InstanceProxy instanceProxy,
    DB.Transform? referencePointTransform
  )
  {
    var definitionId = instanceProxy.definitionId;

    if (_cache.SymbolsByDefinitionId.TryGetValue(definitionId, out var symbol))
    {
      return CreateAndPlaceFamilyInstance(document, instanceProxy, symbol, referencePointTransform);
    }

    _logger.LogWarning("No family symbol found for definition {DefinitionId}", definitionId);
    return null;
  }

  /// <summary>Places one instance of an already-baked artifact-native family symbol (ENG-9101; no InstanceProxy).</summary>
  public FamilyInstance? PlaceInstanceFromArtifact(
    Document document,
    FamilySymbol symbol,
    Matrix4x4 transform,
    string units,
    DB.Transform? referencePointTransform
  ) => CreateAndPlaceFamilyInstance(document, transform, units, symbol, referencePointTransform);

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

  // useFallbackIdWhenUnnamed: false here, preserving the original v1 behavior verbatim (every unnamed definition
  // collapses to the literal "Unnamed_Block", a pre-existing limitation left untouched). The bundle-native overload
  // below defaults to true — it has a cheap, always-unique fallbackId (the bundle definition key) available, so an
  // unnamed artifact definition doesn't need to share that literal with every other unnamed one in the same bundle.
  private static string GetFamilyName(InstanceDefinitionProxy definitionProxy) =>
    GetFamilyName(definitionProxy.name, definitionProxy.id, useFallbackIdWhenUnnamed: false);

  private static string GetFamilyName(string? name, string? fallbackId, bool useFallbackIdWhenUnnamed = true)
  {
    if (string.IsNullOrWhiteSpace(name))
    {
      return useFallbackIdWhenUnnamed && fallbackId is { Length: > 0 } id ? $"Unnamed_Block_{id}" : "Unnamed_Block";
    }
    // net48 reference assemblies lack [NotNullWhen(false)] on IsNullOrWhiteSpace, so assert what the guard proved.
    string baseName = name!;

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
      var shortId = fallbackId?[..8] ?? Guid.NewGuid().ToString("N")[..8];
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
