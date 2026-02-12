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
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Extensions;
using Speckle.Sdk.Models.Instances;
using Document = Autodesk.Revit.DB.Document;
using SMesh = Speckle.Objects.Geometry.Mesh;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Bakes Speckle InstanceProxy/InstanceDefinitionProxy as Revit Families and FamilyInstances.
/// Uses a flattened approach: nested block geometry is recursively collected and baked directly
/// into each family definition, avoiding the complexities of nested family instance placement.
/// </summary>
public sealed class RevitFamilyBaker : IDisposable
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _cache;
  private readonly ILogger<RevitFamilyBaker> _logger;
  private readonly ITypedConverter<(Matrix4x4 matrix, string units), Transform> _transformConverter;
  private readonly RevitMeshBuilder _revitMeshBuilder;

  private string? _cachedTemplatePath;
  private readonly Dictionary<string, string> _bakedFamilyPaths = [];

  private Dictionary<string, InstanceDefinitionProxy> _definitionLookup = [];

  public RevitFamilyBaker(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToHostCacheSingleton cache,
    ILogger<RevitFamilyBaker> logger,
    ITypedConverter<(Matrix4x4 matrix, string units), Transform> transformConverter,
    RevitMeshBuilder revitMeshBuilder
  )
  {
    _converterSettings = converterSettings;
    _cache = cache;
    _logger = logger;
    _transformConverter = transformConverter;
    _revitMeshBuilder = revitMeshBuilder;
  }

  public (List<ReceiveConversionResult> results, List<string> createdElementIds) BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    IReadOnlyDictionary<string, Base> speckleObjectLookup,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var document = _converterSettings.Current.Document;
    var results = new List<ReceiveConversionResult>();
    var createdElementIds = new List<string>();

    // Build definition lookup for nested geometry resolution
    _definitionLookup = instanceComponents
      .Select(x => x.component)
      .OfType<InstanceDefinitionProxy>()
      .ToDictionary(d => d.applicationId ?? d.id.NotNull(), d => d);

    // Process definitions first (sorted by depth - deepest first), then instances
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
          var result = CreateFamilyFromDefinition(document, definitionProxy, speckleObjectLookup);
          if (result.HasValue)
          {
            results.Add(
              new ReceiveConversionResult(Status.SUCCESS, definitionProxy, result.Value.family.Id.ToString(), "Family")
            );
          }
        }
        else if (component is InstanceProxy instanceProxy)
        {
          // Only place top-level instances (those not consumed by other definitions)
          if (!IsConsumedByDefinition(instanceProxy))
          {
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

  /// <summary>
  /// Checks if an InstanceProxy is consumed by a definition (i.e., it's a nested instance).
  /// Nested instances are baked as geometry into parent families, not placed separately.
  /// </summary>
  private bool IsConsumedByDefinition(InstanceProxy instanceProxy)
  {
    var instanceId = instanceProxy.applicationId ?? instanceProxy.id;
    if (instanceId is null)
    {
      return false;
    }
    return _definitionLookup.Values.Any(d => d.objects.Contains(instanceId));
  }

  private (Family family, FamilySymbol symbol)? CreateFamilyFromDefinition(
    Document document,
    InstanceDefinitionProxy definitionProxy,
    IReadOnlyDictionary<string, Base> objectLookup
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
      FindFamilyByName(document, familyName) ?? CreateFamily(document, familyName, definitionProxy, objectLookup);

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

    _cache.FamiliesByDefinitionId[definitionId] = family;
    _cache.SymbolsByDefinitionId[definitionId] = symbol;

    return (family, symbol);
  }

  private Family? CreateFamily(
    Document document,
    string familyName,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, Base> objectLookup
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
        PopulateFamilyFlattened(famDoc, definition, objectLookup, Matrix4x4.Identity);
        SetFamilyWorkPlaneBased(famDoc);
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

  /// <summary>
  /// Populates family with flattened geometry - recursively collects all geometry including from nested instances.
  /// </summary>
  private void PopulateFamilyFlattened(
    Document famDoc,
    InstanceDefinitionProxy definition,
    IReadOnlyDictionary<string, Base> objectLookup,
    Matrix4x4 accumulatedTransform
  )
  {
    if (definition.objects.Count == 0)
    {
      return;
    }

    foreach (var id in definition.objects)
    {
      if (!objectLookup.TryGetValue(id, out var obj))
      {
        _logger.LogWarning("Failed to find object {ObjectId} for definition {DefinitionId}", id, definition.id);
        continue;
      }

      try
      {
        if (obj is InstanceProxy nestedInstanceProxy)
        {
          // Recursively flatten nested instance geometry
          ProcessNestedInstance(famDoc, nestedInstanceProxy, objectLookup, accumulatedTransform);
        }
        else
        {
          // Bake direct geometry with accumulated transform
          BakeGeometryWithTransform(famDoc, obj, accumulatedTransform);
        }
      }
      catch (Autodesk.Revit.Exceptions.ApplicationException ex)
      {
        _logger.LogWarning(ex, "Revit API error baking object {ObjectId} into family {Family}", id, definition.name);
      }
      catch (SpeckleException ex)
      {
        _logger.LogWarning(ex, "Speckle error baking object {ObjectId} into family {Family}", id, definition.name);
      }
    }
  }

  /// <summary>
  /// Processes a nested instance by recursively flattening its definition's geometry.
  /// </summary>
  private void ProcessNestedInstance(
    Document famDoc,
    InstanceProxy nestedInstanceProxy,
    IReadOnlyDictionary<string, Base> objectLookup,
    Matrix4x4 parentTransform
  )
  {
    var nestedDefinitionId = nestedInstanceProxy.definitionId;

    if (!_definitionLookup.TryGetValue(nestedDefinitionId, out var nestedDefinition))
    {
      _logger.LogWarning("Failed to find nested definition {DefinitionId}", nestedDefinitionId);
      return;
    }

    // Compose transforms: parent * nested instance transform
    var composedTransform = parentTransform * nestedInstanceProxy.transform;

    // Recursively populate with composed transform
    PopulateFamilyFlattened(famDoc, nestedDefinition, objectLookup, composedTransform);
  }

  /// <summary>
  /// Bakes geometry into family document with the specified transform applied.
  /// </summary>
  private void BakeGeometryWithTransform(Document famDoc, Base obj, Matrix4x4 transform)
  {
    var meshes = new List<SMesh>();

    if (obj is SMesh mesh)
    {
      meshes.Add(mesh);
    }

    var displayValues = obj.TryGetDisplayValue();
    if (displayValues != null)
    {
      foreach (var item in displayValues)
      {
        if (item is SMesh displayMesh)
        {
          meshes.Add(displayMesh);
        }
      }
    }

    foreach (var m in meshes)
    {
      BakeMeshWithTransform(famDoc, m, transform);
    }
  }

  /// <summary>
  /// Bakes a single mesh into the family document with transform applied.
  /// </summary>
  private void BakeMeshWithTransform(Document famDoc, SMesh mesh, Matrix4x4 transform)
  {
    // Apply transform to mesh if not identity
    var meshToBake = transform == Matrix4x4.Identity ? mesh : TransformMesh(mesh, transform);

    var geomObject = _revitMeshBuilder.BuildFreeformElementGeometry(meshToBake);

    if (geomObject is Solid solid)
    {
      using var _ = FreeFormElement.Create(famDoc, solid);
    }
    else if (geomObject is Mesh revitMesh)
    {
      using var ds = DirectShape.CreateElement(famDoc, new ElementId(BuiltInCategory.OST_GenericModel));
      ds.SetShape([revitMesh]);
    }
  }

  /// <summary>
  /// Creates a transformed copy of a mesh by applying the matrix to all vertices.
  /// </summary>
  private static SMesh TransformMesh(SMesh originalMesh, Matrix4x4 transform)
  {
    var transformedMesh = new SMesh
    {
      vertices = TransformVertices(originalMesh.vertices, transform),
      faces = [.. originalMesh.faces],
      units = originalMesh.units,
      applicationId = originalMesh.applicationId
    };

    if (originalMesh.colors is { } colors)
    {
      transformedMesh.colors = [.. colors];
    }

    return transformedMesh;
  }

  /// <summary>
  /// Transforms a flat list of vertices (x,y,z,x,y,z,...) by the given matrix.
  /// </summary>
  private static List<double> TransformVertices(IReadOnlyList<double> vertices, Matrix4x4 transform)
  {
    var result = new List<double>(vertices.Count);

    for (int i = 0; i < vertices.Count; i += 3)
    {
      double x = vertices[i];
      double y = vertices[i + 1];
      double z = vertices[i + 2];

      // Apply 4x4 transformation
      double newX = transform.M11 * x + transform.M12 * y + transform.M13 * z + transform.M14;
      double newY = transform.M21 * x + transform.M22 * y + transform.M23 * z + transform.M24;
      double newZ = transform.M31 * x + transform.M32 * y + transform.M33 * z + transform.M34;

      result.Add(newX);
      result.Add(newY);
      result.Add(newZ);
    }

    return result;
  }

  /// <summary>
  /// Places family instance in project document using NewFamilyInstances2.
  /// </summary>
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

    // Create SketchPlane at desired orientation (for work-plane-based families)
    var plane = Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(origin, basisX, basisY);
    using var sketchPlane = SketchPlane.Create(document, plane);

    // Use FamilyInstanceCreationData
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

    var instance = document.GetElement(ids.First()) as FamilyInstance;
    if (instance == null)
    {
      return null;
    }

    document.Regenerate();

    var mirrorState = GetMirrorState(instanceProxy.transform);
    ApplyMirroring(document, instance.Id, plane, mirrorState);

    return instance;
  }

  private static void SetFamilyWorkPlaneBased(Document famDoc)
  {
    var workPlaneBasedParam = famDoc.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
    if (workPlaneBasedParam != null && !workPlaneBasedParam.IsReadOnly)
    {
      workPlaneBasedParam.Set(1);
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
    Autodesk.Revit.DB.Plane plane,
    (bool X, bool Y, bool Z) mirrorState
  )
  {
    var mirrorOperations = new List<(string name, bool shouldMirror, Autodesk.Revit.DB.Plane mirrorPlane)>
    {
      ("YZ", mirrorState.X, Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.YVec, plane.Normal)),
      ("XZ", mirrorState.Y, Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.Normal)),
      ("XY", mirrorState.Z, Autodesk.Revit.DB.Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.YVec))
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
    _definitionLookup.Clear();
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
