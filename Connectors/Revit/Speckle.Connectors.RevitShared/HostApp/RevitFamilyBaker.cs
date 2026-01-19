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
/// Expects to be a scoped dependency per receive operation.
/// </summary>
/// <remarks>
/// Depth-aware processing for nested blocks.
/// </remarks>
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
    IProgress<CardProgress> onOperationProgressed
  )
  {
    var document = _converterSettings.Current.Document;
    var results = new List<ReceiveConversionResult>();
    var createdElementIds = new List<string>();

    // sort by maxDepth descending, definitions before instances
    // TODO: check in on GH
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
          var result = CreateFamilyFromDefinition(document, definitionProxy);
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

        // only add error result if we have a valid Base object
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
  /// </summary>
  private (Family family, FamilySymbol symbol)? CreateFamilyFromDefinition(
    Document document,
    InstanceDefinitionProxy definitionProxy
  )
  {
    // NOTE: for this ticket just creates an empty family with placeholder geometry (bounding box).
    var definitionId = definitionProxy.applicationId ?? definitionProxy.id.NotNull();

    // check cache first
    if (_cache.FamiliesByDefinitionId.TryGetValue(definitionId, out var existingFamily))
    {
      var existingSymbol = _cache.SymbolsByDefinitionId[definitionId];
      return (existingFamily, existingSymbol);
    }

    // check if family already exists in document, creates if doesn't exist
    // TODO: this is where we need to talk about update behavior
    var familyName = GetFamilyName(definitionProxy);
    var family = FindFamilyByName(document, familyName) ?? CreateFamily(document, familyName);

    if (family == null)
    {
      _logger.LogWarning("Failed to create family for definition {DefinitionId}", definitionId);
      return null;
    }

    // get and activate the first symbol
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

    // cache for future use
    _cache.FamiliesByDefinitionId[definitionId] = family;
    _cache.SymbolsByDefinitionId[definitionId] = symbol;

    return (family, symbol);
  }

  /// <summary>
  /// Gets the family name for a definition proxy, sanitized for use as a filename.
  /// </summary>
  private static string GetFamilyName(InstanceDefinitionProxy definitionProxy)
  {
    var baseName = definitionProxy.name;

    // remove invalid filename characters
    var invalidChars = Path.GetInvalidFileNameChars(); // e.g. :, \, /, *, ?
    return string.Concat(baseName.Select(c => invalidChars.Contains(c) ? '_' : c));
  }

  /// <summary>
  /// Finds a family by name in the document.
  /// </summary>
  private static Family? FindFamilyByName(Document document, string familyName)
  {
    using var collector = new FilteredElementCollector(document);
    return collector.OfClass(typeof(Family)).Cast<Family>().FirstOrDefault(f => f.Name == familyName);
  }

  /// <summary>
  /// Creates a new family document, adds placeholder geometry, sets work-plane-based, saves and loads it.
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
        SetFamilyWorkPlaneBased(famDoc); // enable work-plane-based placement
        t.Commit();
      }

      var tempPath = Path.Combine(Path.GetTempPath(), $"{familyName}.rfa");
      var saveOptions = new SaveAsOptions { OverwriteExistingFile = true };
      famDoc.SaveAs(tempPath, saveOptions);
      famDoc.Close(false);

      document.LoadFamily(tempPath, new FamilyLoadOptions(), out var loadedFamily);

      // clean up temp file and ignore errors
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
  /// Gets the path to the Generic Model family template.
  /// Searches multiple locations including Revit's default template paths and language-specific folders.
  /// </summary>
  /// <remarks>
  /// Watchout for language-specific template names.
  /// </remarks>
  private string GetFamilyTemplatePath(Document document)
  {
    // TODO: I (Björn) am not happy with my hack here.
    // Improve before merging working branch to dev

    // template names to search for (in order of preference)
    var templateNames = new[]
    {
      "Generic Model.rft",
      "Metric Generic Model.rft",
      "Allgemeines Modell.rft", // German
      "Modèle générique.rft", // French
      "Modelo genérico.rft", // Spanish
    };

    // collect all potential template folders
    var templateFolders = new List<string>();

    // 1. Revit's configured FamilyTemplatePath
    var configuredPath = document.Application.FamilyTemplatePath;
    if (!string.IsNullOrEmpty(configuredPath) && Directory.Exists(configuredPath))
    {
      templateFolders.Add(configuredPath);
    }

    // 2. Default Revit installation paths based on version
    var revitVersion = document.Application.VersionNumber;
    var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    var defaultPath = Path.Combine(programData, "Autodesk", $"RVT {revitVersion}", "Family Templates");
    if (Directory.Exists(defaultPath))
    {
      templateFolders.Add(defaultPath);
    }

    // 3. Alternative default paths
    var altDefaultPaths = new[]
    {
      Path.Combine(programData, "Autodesk", $"RVT {revitVersion}", "Family Templates", "English"),
      Path.Combine(programData, "Autodesk", $"RVT {revitVersion}", "Family Templates", "English-Imperial"),
      Path.Combine(programData, "Autodesk", $"RVT {revitVersion}", "Family Templates", "English_I"),
      Path.Combine(programData, "Autodesk", $"RVT {revitVersion}", "Family Templates", "US Imperial"),
      Path.Combine(programData, "Autodesk", $"RVT {revitVersion}", "Family Templates", "US Metric"),
    };

    foreach (var path in altDefaultPaths)
    {
      if (Directory.Exists(path))
      {
        templateFolders.Add(path);
      }
    }

    // Search for templates in all folders
    foreach (var folder in templateFolders)
    {
      // Direct match
      foreach (var templateName in templateNames)
      {
        var directPath = Path.Combine(folder, templateName);
        if (File.Exists(directPath))
        {
          _logger.LogDebug("Found family template at {TemplatePath}", directPath);
          return directPath;
        }
      }

      // Search subdirectories
      foreach (var templateName in templateNames)
      {
        try
        {
          var files = Directory.GetFiles(folder, templateName, SearchOption.AllDirectories);
          if (files.Length > 0)
          {
            _logger.LogDebug("Found family template at {TemplatePath}", files[0]);
            return files[0];
          }
        }
        catch (UnauthorizedAccessException)
        {
          // Skip folders we can't access
        }
      }
    }

    // Last resort: search any .rft file that contains "generic" in the name
    foreach (var folder in templateFolders)
    {
      try
      {
        var genericTemplates = Directory
          .GetFiles(folder, "*.rft", SearchOption.AllDirectories)
          .Where(f => Path.GetFileName(f).IndexOf("generic", StringComparison.OrdinalIgnoreCase) >= 0)
          .ToArray();

        if (genericTemplates.Length > 0)
        {
          _logger.LogDebug("Found generic family template at {TemplatePath}", genericTemplates[0]);
          return genericTemplates[0];
        }
      }
      catch (UnauthorizedAccessException)
      {
        // Skip folders we can't access
      }
    }

    _logger.LogWarning(
      "Could not find Generic Model template. Searched folders: {Folders}",
      string.Join(", ", templateFolders)
    );
    return string.Empty;
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
  /// Sets the family to be work-plane-based, which allows proper 3D placement with full transform support.
  /// </summary>
  private static void SetFamilyWorkPlaneBased(Document famDoc)
  {
    var workPlaneBasedParam = famDoc.OwnerFamily.get_Parameter(BuiltInParameter.FAMILY_WORK_PLANE_BASED);
    if (workPlaneBasedParam != null && !workPlaneBasedParam.IsReadOnly)
    {
      workPlaneBasedParam.Set(1); // 1 = true/checked
    }
  }

  /// <summary>
  /// Places a FamilyInstance for the given InstanceProxy with full transform support including rotation and mirroring.
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

    // convert transform and extract mirroring info
    var revitTransform = ConvertTransform(instanceProxy.transform, instanceProxy.units);
    var mirrorState = GetMirrorState(instanceProxy.transform);

    // get placement plane from transform
    var position = revitTransform.Origin;
    var instanceXAxis = revitTransform.BasisX;
    var instanceYAxis = revitTransform.BasisY;
    using var plane = Plane.CreateByOriginAndBasis(position, instanceXAxis, instanceYAxis);

    // create reference plane for work-plane based placement (matches legacy approach)
    var refPlane = document.Create.NewReferencePlane2(
      plane.Origin,
      plane.Origin + plane.XVec,
      plane.Origin + plane.YVec,
      document.ActiveView
    );

    // place the instance on the reference plane
    var instance = document.Create.NewFamilyInstance(refPlane.GetReference(), plane.Origin, plane.XVec, symbol);

    // apply mirroring if needed
    ApplyMirroring(document, instance.Id, plane, mirrorState);

    return instance;
  }

  /// <summary>
  /// Extracts mirror state from the transform matrix by checking the determinant.
  /// A negative determinant indicates a reflection (odd number of axis flips).
  /// </summary>
  private static (bool X, bool Y, bool Z) GetMirrorState(Matrix4x4 matrix)
  {
    // Check determinant of the 3x3 rotation/scale part to detect reflection
    var det =
      matrix.M11 * (matrix.M22 * matrix.M33 - matrix.M23 * matrix.M32)
      - matrix.M12 * (matrix.M21 * matrix.M33 - matrix.M23 * matrix.M31)
      + matrix.M13 * (matrix.M21 * matrix.M32 - matrix.M22 * matrix.M31);

    // If determinant is negative, there's a reflection
    // We apply X-axis mirroring as that's the most common case
    if (det < 0)
    {
      return (true, false, false);
    }

    return (false, false, false);
  }

  /// <summary>
  /// Mirrors an element across the specified axes of a given plane.
  /// </summary>
  private void ApplyMirroring(Document document, ElementId elementId, Plane plane, (bool X, bool Y, bool Z) mirrorState)
  {
    var mirrorOperations = new List<(string name, bool shouldMirror, Plane mirrorPlane)>
    {
      ("YZ", mirrorState.X, Plane.CreateByOriginAndBasis(plane.Origin, plane.YVec, plane.Normal)),
      ("XZ", mirrorState.Y, Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.Normal)),
      ("XY", mirrorState.Z, Plane.CreateByOriginAndBasis(plane.Origin, plane.XVec, plane.YVec))
    };

    foreach (var (name, shouldMirror, mirrorPlane) in mirrorOperations.Where(op => op.shouldMirror))
    {
      try
      {
        document.Regenerate();
        ElementTransformUtils.MirrorElements(document, new List<ElementId> { elementId }, mirrorPlane, false);
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
