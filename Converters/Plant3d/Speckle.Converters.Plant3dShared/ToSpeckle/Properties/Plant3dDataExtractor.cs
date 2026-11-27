using Autodesk.ProcessPower.PlantInstance;
using Autodesk.ProcessPower.ProjectManager;
using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Converters.Plant3dShared.ToSpeckle;

/// <summary>
/// Extracts Plant3D project database properties for entities.
/// Uses the PnPDataLinks API to find the data row linked to a DWG entity,
/// then reads all properties from that row in the project database (DCF files).
/// </summary>
public class Plant3dDataExtractor
{
  private readonly IConverterSettingsStore<Plant3dConversionSettings> _settingsStore;
  private readonly Plant3dClassHierarchyResolver _classHierarchyResolver;
  private readonly Plant3dLineGroupResolver _lineGroupResolver;
  private readonly ILogger<Plant3dDataExtractor> _logger;
  private bool _dataLinksManagerResolved;
  private PPDL.DataLinksManager? _dataLinksManager;
  private Plant3dDrawingType _drawingType;

  public Plant3dDataExtractor(
    IConverterSettingsStore<Plant3dConversionSettings> settingsStore,
    Plant3dClassHierarchyResolver classHierarchyResolver,
    Plant3dLineGroupResolver lineGroupResolver,
    ILogger<Plant3dDataExtractor> logger
  )
  {
    _settingsStore = settingsStore;
    _classHierarchyResolver = classHierarchyResolver;
    _lineGroupResolver = lineGroupResolver;
    _logger = logger;
  }

  /// <summary>
  /// Gets Plant3D database properties for the given entity.
  /// Returns a dictionary of property name → value pairs from the project database.
  /// </summary>
#pragma warning disable CA1031 // Plant3D data APIs can throw various exceptions
  public Dictionary<string, object?> GetDataProperties(ADB.Entity entity)
  {
    var result = new Dictionary<string, object?>();

    try
    {
      var (dlm, drawingType) = GetDataLinksManager();
      if (dlm is null)
      {
        return result;
      }

      // Find the data row linked to this entity (returns single int row ID)
      if (!TryGetRowId(dlm, entity, out var rowId))
      {
        return result;
      }

      // Read all properties from the linked row
      var allProps = dlm.GetAllProperties(rowId, true);
      if (allProps is not null)
      {
        foreach (var kvp in allProps)
        {
          string key = kvp.Key ?? "unknown";

          result[key] = kvp.Value;
        }
      }

      AddDataManagerProperties(dlm, drawingType, entity, rowId, result);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      // DataLinksManager unavailable, entity unlinked/orphaned from the PnP project database,
      // or any other DataLinks API failure — degrade to no P&ID properties instead of failing the object.
      _logger.LogWarning(ex, "Failed to read PnP DataLinks properties on object {HandleValue}", entity.Handle.Value);
    }

    return result;
  }

  private (PPDL.DataLinksManager? Manager, Plant3dDrawingType DrawingType) GetDataLinksManager()
  {
    if (!_dataLinksManagerResolved)
    {
      var doc = _settingsStore.Current.Document;
      (_dataLinksManager, _drawingType) = TryGetProjectDataLinksManager(doc) ?? GetDocumentDataLinksManager(doc);
      _dataLinksManagerResolved = _dataLinksManager is not null;
    }
    return (_dataLinksManager, _drawingType);
  }

  private (PPDL.DataLinksManager Manager, Plant3dDrawingType DrawingType)? TryGetProjectDataLinksManager(Document doc)
  {
    try
    {
      if (
        PnPProjectUtils.DoesDocumentBelongToCurrentProject(doc, out var projectPart, out _) == DwgProject.eOk
        && PlantApplication.ProjectPart(projectPart) is Project project
        && project.DataLinksManager is not null
      )
      {
        return (project.DataLinksManager, Plant3dDrawingTypeExtensions.FromProjectPart(projectPart));
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogWarning(ex, "Failed to resolve the project DataLinksManager, falling back to the document manager");
    }
    return null;
  }
#pragma warning restore CA1031

  private static (PPDL.DataLinksManager? Manager, Plant3dDrawingType DrawingType) GetDocumentDataLinksManager(
    Document doc
  ) =>
    (
      PPDL.DataLinksManager.GetManager(doc.Database),
      Plant3dDrawingTypeExtensions.FromProjectPart(PnPProjectUtils.GetDocumentProjectPartType(doc))
    );

  private static bool TryGetRowId(PPDL.DataLinksManager dataLinksManager, ADB.Entity entity, out int rowId)
  {
    // Checking HasLinks avoids many exceptions where the entity is not Plant object e.g. lines, text, block references
    rowId = dataLinksManager.HasLinks(entity.ObjectId) ? dataLinksManager.FindAcPpRowId(entity.ObjectId) : 0;
    return rowId > 0;
  }

  private void AddDataManagerProperties(
    PPDL.DataLinksManager dataLinksManager,
    Plant3dDrawingType drawingType,
    ADB.Entity entity,
    int rowId,
    Dictionary<string, object?> plant3dSection
  )
  {
    var classHierarchy = _classHierarchyResolver.Resolve(dataLinksManager, entity.ObjectId);
    var hasGroup = _lineGroupResolver.TryGetGroupInfo(dataLinksManager, drawingType, entity, rowId, out var groupInfo);

    if (classHierarchy is null && !hasGroup)
    {
      return;
    }

    var dataManagerSection = Plant3dDataManagerProperties.CreateDataManagerSection(plant3dSection);

    if (classHierarchy is not null)
    {
      Plant3dDataManagerProperties.WriteCategory(dataManagerSection, classHierarchy.Category, classHierarchy.Levels);
    }

    if (groupInfo is not null)
    {
      Plant3dDataManagerProperties.WriteLineGroup(dataManagerSection, groupInfo);
    }
  }
}
