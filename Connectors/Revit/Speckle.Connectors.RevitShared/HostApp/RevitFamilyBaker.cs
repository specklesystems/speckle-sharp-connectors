using System.IO;
using Autodesk.Revit.DB;
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
  private readonly ITypedConverter<(Matrix4x4 matrix, string units), Transform> _transformConverter;
  private string? _cachedTemplatePath;

  public RevitFamilyBaker(
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToHostCacheSingleton cache,
    ILogger<RevitFamilyBaker> logger,
    ITypedConverter<(Matrix4x4 matrix, string units), Transform> transformConverter
  )
  {
    _converterSettings = converterSettings;
    _cache = cache;
    _logger = logger;
    _transformConverter = transformConverter;
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
        string componentId = component switch
        {
          InstanceDefinitionProxy d => d.applicationId ?? d.id.NotNull(),
          InstanceProxy i => i.applicationId ?? i.id.NotNull(),
          _ => "unknown"
        };

        _logger.LogError(ex, "Failed to process instance component {ComponentId}", componentId);

        switch (component)
        {
          case InstanceDefinitionProxy defProxy:
            results.Add(new ReceiveConversionResult(Status.ERROR, defProxy, null, null, ex));
            break;
          case InstanceProxy instProxy:
            results.Add(new ReceiveConversionResult(Status.ERROR, instProxy, null, null, ex));
            break;
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
    return collector.OfClass(typeof(Family)).OfType<Family>().FirstOrDefault(f => f.Name == familyName);
  }

  /// <summary>
  /// Creates a new family document, adds placeholder geometry, sets work-plane-based, saves and loads it.
  /// </summary>
  private Family? CreateFamily(Document document, string familyName)
  {
    var templatePath = GetFamilyTemplatePath(document); // throws if file doesn't exist
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
  /// Resolves the physical path to the appropriate Revit family template (.rft) based on the document's unit system and Revit version.
  /// </summary>
  private string GetFamilyTemplatePath(Document document)
  {
    // return cached path if we've already found it during this receive operation
    if (_cachedTemplatePath != null)
    {
      return _cachedTemplatePath;
    }

    // read version
    var version = document.Application.VersionNumber;

    // check if doc is Metric or Imperial
    var isMetric = document.DisplayUnitSystem == DisplayUnit.METRIC;
    var templateName = isMetric ? "Metric Generic Model.rft" : "Generic Model.rft";

    // resolve the folder where the DLL lives
    // typeof() anchors the Resources search to the actual deployment directory of the connector
    // should be most robust for local and installed state(s)
    var assemblyLocation = typeof(RevitFamilyBaker).Assembly.Location;
    var assemblyDir =
      Path.GetDirectoryName(assemblyLocation) ?? throw new ConversionException("Could not resolve assembly directory.");

    // using the same structure from .projitems creates: Resources/Templates/{Year}/{File}
    var templatePath = Path.Combine(assemblyDir, "Resources", "Templates", version, templateName);

    // fail loudly if nothing found
    if (!File.Exists(templatePath))
    {
      _logger.LogError("Revit Family Template missing. Searched path: {templatePath}", templatePath);

      throw new ConversionException(
        $"Could not find required family template: {templateName}. "
          + $"Please ensure the 'Resources' folder exists at {assemblyDir}"
      );
    }

    _cachedTemplatePath = templatePath;
    return templatePath;
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
  /// Places a FamilyInstance for the given InstanceProxy using a work-plane-based strategy
  /// to support full 3D transforms, including rotation and mirroring.
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

    // create a Reference Plane
    XYZ bubbleEnd = revitTransform.Origin + revitTransform.BasisX;
    XYZ freeEnd = revitTransform.Origin + revitTransform.BasisY;

    ReferencePlane refPlane = document.Create.NewReferencePlane2(
      bubbleEnd,
      revitTransform.Origin,
      freeEnd,
      document.ActiveView
    );

    // place using the reference from the ReferencePlane
    var instance = document.Create.NewFamilyInstance(
      refPlane.GetReference(),
      revitTransform.Origin,
      revitTransform.BasisX,
      symbol
    );

    // handle mirroring
    var mirrorState = GetMirrorState(instanceProxy.transform);
    ApplyMirroring(document, instance.Id, refPlane.GetPlane(), mirrorState);

    // NOTE: we leave the ReferencePlane for now to ensure stability.
    // TODO: collect these and delete them at the very end of the BakeInstances loop to keep the file clean

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

  private sealed class FamilyLoadOptions : IFamilyLoadOptions
  {
    public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
    {
      // if the family exists, overwrite its parameter values with the incoming ones
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
      // FamilySource.Family means "Use the version from the RFA file being loaded" (I think)
      // this ensures shared components update to match the Speckle data.
      source = FamilySource.Family;
      overwriteParameterValues = true;
      return true;
    }
  }
}
