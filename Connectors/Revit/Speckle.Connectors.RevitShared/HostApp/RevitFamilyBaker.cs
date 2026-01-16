using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.DoubleNumerics;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Plane = Autodesk.Revit.DB.Plane;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Bakes InstanceProxy and InstanceDefinitionProxy objects as Revit Families and FamilyInstances.
/// Handles depth-aware processing for nested blocks.
/// Expects to be a scoped dependency per receive operation.
/// </summary>
public class RevitFamilyBaker
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitToHostCacheSingleton _cache;
  private readonly ILogger<RevitFamilyBaker> _logger;

  public RevitFamilyBaker(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToHostCacheSingleton cache,
    ILogger<RevitFamilyBaker> logger
  )
  {
    _converterSettings = converterSettings;
    _cache = cache;
    _logger = logger;
  }

  /// <summary>
  /// Bakes instance definitions as Families and instance proxies as FamilyInstances.
  /// Processes in depth order (deepest first) to handle nested blocks correctly.
  /// </summary>
  public (List<ReceiveConversionResult> results, List<string> createdElementIds) BakeInstances(
    ICollection<(Collection[] collectionPath, IInstanceComponent component)> instanceComponents,
    string baseLayerName,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var document = _converterSettings.Current.Document;
    var results = new List<ReceiveConversionResult>();
    var createdElementIds = new List<string>();

    // Sort by maxDepth descending, definitions before instances
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
          var result = CreateFamilyFromDefinition(document, definitionProxy, baseLayerName);
          if (result.HasValue)
          {
            results.Add(
              new ReceiveConversionResult(Status.SUCCESS, definitionProxy, result.Value.family.Id.ToString(), "Family")
            );
          }
        }
        else if (component is InstanceProxy instanceProxy)
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
      catch (Exception ex) when (!ex.IsFatal())
      {
        var componentId =
          (component as InstanceDefinitionProxy)?.applicationId
          ?? (component as InstanceProxy)?.applicationId
          ?? "unknown";
        _logger.LogError(ex, "Failed to process instance component {ComponentId}", componentId);

        // Only add error result if we have a valid Base object
        if (component is InstanceDefinitionProxy defProxy)
        {
          results.Add(new ReceiveConversionResult(Status.ERROR, defProxy, null, null, ex));
        }
        else if (component is InstanceProxy instProxy)
        {
          results.Add(new ReceiveConversionResult(Status.ERROR, instProxy, null, null, ex));
        }
      }
    }

    return (results, createdElementIds);
  }

  /// <summary>
  /// Creates a Revit Family from an InstanceDefinitionProxy.
  /// For this ticket: creates an empty family with placeholder geometry (bounding box).
  /// </summary>
  private (Family family, FamilySymbol symbol)? CreateFamilyFromDefinition(
    Document document,
    InstanceDefinitionProxy definitionProxy,
    string baseLayerName
  )
  {
    var definitionId = definitionProxy.applicationId ?? definitionProxy.id.NotNull();

    // Check cache first
    if (_cache.FamiliesByDefinitionId.TryGetValue(definitionId, out var existingFamily))
    {
      var existingSymbol = _cache.SymbolsByDefinitionId[definitionId];
      return (existingFamily, existingSymbol);
    }

    // Check if family already exists in document
    var familyName = GetFamilyName(definitionProxy, baseLayerName);
    var family = FindFamilyByName(document, familyName) ?? CreateFamily(document, familyName);

    if (family == null)
    {
      _logger.LogWarning("Failed to create family for definition {DefinitionId}", definitionId);
      return null;
    }

    // Get and activate the first symbol
    var symbolId = family.GetFamilySymbolIds().FirstOrDefault();
    if (symbolId == null || symbolId == ElementId.InvalidElementId)
    {
      _logger.LogWarning("Family {FamilyName} has no symbols", familyName);
      return null;
    }

    var symbol = document.GetElement(symbolId) as FamilySymbol;
    if (symbol == null)
    {
      return null;
    }

    if (!symbol.IsActive)
    {
      symbol.Activate();
      document.Regenerate();
    }

    // Cache for future use
    _cache.FamiliesByDefinitionId[definitionId] = family;
    _cache.SymbolsByDefinitionId[definitionId] = symbol;

    return (family, symbol);
  }

  /// <summary>
  /// Creates a new family document, adds placeholder geometry, saves and loads it.
  /// </summary>
  private Family? CreateFamily(Document document, string familyName)
  {
    var templatePath = GetFamilyTemplatePath(document);
    if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
    {
      throw new ConversionException($"Could not find family template at: {templatePath}");
    }

    var famDoc = document.Application.NewFamilyDocument(templatePath);

    try
    {
      using (var t = new Transaction(famDoc, "Create placeholder geometry"))
      {
        t.Start();
        CreatePlaceholderGeometry(famDoc);
        t.Commit();
      }

      var tempPath = Path.Combine(Path.GetTempPath(), $"{familyName}.rfa");
      var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
      famDoc.SaveAs(tempPath, saveOptions);
      famDoc.Close(false);

      document.LoadFamily(tempPath, new FamilyLoadOptions(), out var loadedFamily);

      // Clean up temp file - ignore errors
      try
      {
        File.Delete(tempPath);
      }
      catch (IOException) { }

      return loadedFamily;
    }
    catch
    {
      famDoc.Close(false);
      throw;
    }
  }

  /// <summary>
  /// Creates placeholder geometry (1x1x1 foot box) for the family.
  /// </summary>
  private static void CreatePlaceholderGeometry(Document famDoc)
  {
    const double SIZE = 1.0; // 1 foot

    var profile = new CurveLoop();
    var p0 = new XYZ(-SIZE / 2, -SIZE / 2, 0);
    var p1 = new XYZ(SIZE / 2, -SIZE / 2, 0);
    var p2 = new XYZ(SIZE / 2, SIZE / 2, 0);
    var p3 = new XYZ(-SIZE / 2, SIZE / 2, 0);

    profile.Append(Line.CreateBound(p0, p1));
    profile.Append(Line.CreateBound(p1, p2));
    profile.Append(Line.CreateBound(p2, p3));
    profile.Append(Line.CreateBound(p3, p0));

    var solid = GeometryCreationUtilities.CreateExtrusionGeometry(new List<CurveLoop> { profile }, XYZ.BasisZ, SIZE);

    using var _ = FreeFormElement.Create(famDoc, solid);
  }

  /// <summary>
  /// Places a FamilyInstance for the given InstanceProxy.
  /// </summary>
  private FamilyInstance? PlaceFamilyInstance(Document document, InstanceProxy instanceProxy)
  {
    var definitionId = instanceProxy.definitionId;

    if (!_cache.SymbolsByDefinitionId.TryGetValue(definitionId, out var symbol))
    {
      _logger.LogWarning(
        "No family symbol found for definition {DefinitionId}. Instance {InstanceId} will be skipped.",
        definitionId,
        instanceProxy.applicationId
      );
      return null;
    }

    // Convert transform
    var revitTransform = ConvertTransform(instanceProxy.transform, instanceProxy.units);

    // Get placement plane from transform
    var origin = revitTransform.Origin;
    var xVec = revitTransform.BasisX;
    var yVec = revitTransform.BasisY;
    using var plane = Plane.CreateByOriginAndBasis(origin, xVec, yVec);

    // Create reference plane for work-plane based placement
    var refPlane = document.Create.NewReferencePlane2(
      plane.Origin,
      plane.Origin + plane.XVec,
      plane.Origin + plane.YVec,
      document.ActiveView
    );

    // Place the instance
    var instance = document.Create.NewFamilyInstance(refPlane.GetReference(), plane.Origin, plane.XVec, symbol);

    return instance;
  }

  /// <summary>
  /// Converts a Speckle Matrix4x4 to a Revit Transform.
  /// </summary>
  private Transform ConvertTransform(Matrix4x4 matrix, string units)
  {
    var transform = Transform.Identity;

    if (matrix.M44 == 0)
    {
      return transform;
    }

    var scaleFactor = GetScaleFactor(units);

    // Translation (with unit scaling)
    var tX = (matrix.M14 / matrix.M44) * scaleFactor;
    var tY = (matrix.M24 / matrix.M44) * scaleFactor;
    var tZ = (matrix.M34 / matrix.M44) * scaleFactor;
    transform.Origin = new XYZ(tX, tY, tZ);

    // Basis vectors (normalized)
    transform.BasisX = new XYZ(matrix.M11, matrix.M21, matrix.M31).Normalize();
    transform.BasisY = new XYZ(matrix.M12, matrix.M22, matrix.M32).Normalize();
    transform.BasisZ = new XYZ(matrix.M13, matrix.M23, matrix.M33).Normalize();

    // Apply reference point transform if set
    var refPointTransform = _converterSettings.Current.ReferencePointTransform;
    if (refPointTransform != null)
    {
      transform = refPointTransform.Multiply(transform);
    }

    return transform;
  }

  /// <summary>
  /// Gets scale factor to convert from source units to Revit internal units (feet).
  /// </summary>
  private static double GetScaleFactor(string units) =>
    units.ToLower() switch
    {
      "mm" or "millimeters" or "millimetres" => 1.0 / 304.8,
      "cm" or "centimeters" or "centimetres" => 1.0 / 30.48,
      "m" or "meters" or "metres" => 1.0 / 0.3048,
      "in" or "inches" => 1.0 / 12.0,
      _ => 1.0
    };

  /// <summary>
  /// Finds a family by name in the document.
  /// </summary>
  private static Family? FindFamilyByName(Document document, string familyName)
  {
    using var collector = new FilteredElementCollector(document);
    return collector.OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == familyName);
  }

  /// <summary>
  /// Gets the family name for a definition proxy.
  /// </summary>
  private static string GetFamilyName(InstanceDefinitionProxy definitionProxy, string baseLayerName)
  {
    var baseName = definitionProxy.name;
    var definitionId = definitionProxy.applicationId ?? definitionProxy.id ?? Guid.NewGuid().ToString();
    return $"{baseName}-({definitionId})-{baseLayerName}";
  }

  /// <summary>
  /// Gets the path to the Generic Model family template.
  /// </summary>
  private string GetFamilyTemplatePath(Document document)
  {
    var templateFolder = document.Application.FamilyTemplatePath;

    var templateNames = new[] { "Generic Model.rft", "Metric Generic Model.rft" };

    foreach (var templateName in templateNames)
    {
      var path = Path.Combine(templateFolder, templateName);
      if (File.Exists(path))
      {
        return path;
      }
    }

    // Search subdirectories
    if (Directory.Exists(templateFolder))
    {
      foreach (var templateName in templateNames)
      {
        var files = Directory.GetFiles(templateFolder, templateName, SearchOption.AllDirectories);
        if (files.Length > 0)
        {
          return files[0];
        }
      }
    }

    _logger.LogWarning("Could not find Generic Model template in {TemplateFolder}", templateFolder);
    return string.Empty;
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
