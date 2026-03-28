using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Conversion;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;
using Speckle.Sdk.Pipelines.Progress;
using Material = Autodesk.AutoCAD.DatabaseServices.Material;
using RenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
[GenerateAutoInterface]
public class AutocadMaterialBaker : IAutocadMaterialBaker
{
  private readonly ILogger<AutocadMaterialBaker> _logger;
  private readonly AutocadContext _autocadContext;
  private Document Doc => Application.DocumentManager.MdiActiveDocument;
  public Dictionary<string, ObjectId> ObjectMaterialsIdMap { get; } = new();

  public AutocadMaterialBaker(AutocadContext autocadContext, ILogger<AutocadMaterialBaker> logger)
  {
    _autocadContext = autocadContext;
    _logger = logger;
  }

  /// <summary>
  /// Try to get material id from original object or its parent (if provided) as fallback).
  /// It covers one-to-many problem, i.e.
  ///  - rhino: Brep (material id is extracted into render material proxy objects) -> [Mesh, Mesh, ...] (child objects application ids ARE NOT EXIST in render material proxy objects)
  ///  - revit : RevitElement (material IS NOT extracted into render material proxy objects) -> [Mesh, Mesh...] (child objects application ids EXIST in render material proxy objects)
  /// </summary>
  /// <remarks>
  /// This is a question that we need to answer where to handle these cases.
  /// We alsa do reverse search for layer render materials on Revit Receive, and mutating the proxy list accordingly.
  /// These cases are increasing, and need some ideation around it before going more messy.
  /// </remarks>
  public bool TryGetMaterialId(Base originalObject, Base? parentObject, out ObjectId materialId)
  {
    materialId = ObjectId.Null;
    var originalObjectId = originalObject.applicationId ?? originalObject.id.NotNull();
    if (ObjectMaterialsIdMap.TryGetValue(originalObjectId, out ObjectId originalObjectMaterialId))
    {
      materialId = originalObjectMaterialId;
      return true;
    }

    if (parentObject is null)
    {
      return false;
    }

    var subObjectId = parentObject.applicationId ?? parentObject.id.NotNull();
    if (ObjectMaterialsIdMap.TryGetValue(subObjectId, out ObjectId subObjectMaterialId))
    {
      materialId = subObjectMaterialId;
      return true;
    }

    return false;
  }

  /// <summary>
  /// Removes all materials with a name starting with <paramref name="namePrefix"/> from the active document
  /// </summary>
  /// <param name="namePrefix"></param>
  public void PurgeMaterials(string namePrefix)
  {
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();
    if (transaction.GetObject(Doc.Database.MaterialDictionaryId, OpenMode.ForWrite) is DBDictionary materialDict)
    {
      foreach (var entry in materialDict)
      {
        try
        {
          if (entry.Key.Contains(namePrefix))
          {
            materialDict.Remove(entry.Value);
          }
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          _logger.LogError(ex, "Failed to purge a material from the document");
        }
      }
    }
    transaction.Commit();
  }

  public void ParseAndBakeRenderMaterials(
    IReadOnlyCollection<RenderMaterialProxy> materialProxies,
    string baseLayerPrefix,
    IProgress<CardProgress> onOperationProgressed
  )
  {
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();
    var materialDict = transaction.GetObject(Doc.Database.MaterialDictionaryId, OpenMode.ForWrite) as DBDictionary;

    if (materialDict == null)
    {
      // POC: we should report failed conversion here if material dict is not accessible, but it is not linked to a Base source
      transaction.Commit();
      return;
    }

    var count = 0;
    foreach (RenderMaterialProxy materialProxy in materialProxies)
    {
      onOperationProgressed.Report(new("Converting render materials", (double)++count / materialProxies.Count));

      // bake render material
      RenderMaterial renderMaterial = materialProxy.value;
      string renderMaterialId = renderMaterial.applicationId ?? renderMaterial.id.NotNull();
      ObjectId materialId = ObjectId.Null;

      if (!ObjectMaterialsIdMap.TryGetValue(renderMaterialId, out materialId))
      {
        (materialId, ReceiveConversionResult result) = BakeMaterial(
          renderMaterial,
          baseLayerPrefix,
          materialDict,
          transaction
        );
      }

      if (materialId == ObjectId.Null)
      {
        continue;
      }

      // parse render material object ids
      foreach (string objectId in materialProxy.objects)
      {
        ObjectMaterialsIdMap[objectId] = materialId;
      }
    }

    transaction.Commit();
  }

  private (ObjectId, ReceiveConversionResult) BakeMaterial(
    RenderMaterial renderMaterial,
    string baseLayerPrefix,
    DBDictionary materialDict,
    Transaction tr
  )
  {
    ObjectId materialId = ObjectId.Null;

    try
    {
      // POC: Currently we're relying on the render material name for identification if it's coming from speckle and from which model; could we do something else?
      // POC: we should assume render materials all have application ids?
      string renderMaterialId = renderMaterial.applicationId ?? renderMaterial.id.NotNull();
      string matName = _autocadContext.RemoveInvalidChars(
        $"{renderMaterial.name}-({renderMaterialId})-{baseLayerPrefix}"
      );

      MaterialMap map = new();
      MaterialOpacityComponent opacity = new(renderMaterial.opacity, map);
      var systemDiffuse = System.Drawing.Color.FromArgb(renderMaterial.diffuse);
      EntityColor entityDiffuseColor = new(systemDiffuse.R, systemDiffuse.G, systemDiffuse.B);
      MaterialColor diffuseColor = new(Method.Override, 1, entityDiffuseColor);
      MaterialDiffuseComponent diffuse = new(diffuseColor, map);

      Material mat =
        new()
        {
          Name = matName,
          Opacity = opacity,
          Diffuse = diffuse
        };

      if (renderMaterial["reflectivity"] is double reflectivity)
      {
        mat.Reflectivity = reflectivity;
      }

      if (renderMaterial["ior"] is double ior)
      {
        mat.Refraction = new(ior, map);
      }

      // POC: assumes all materials with this prefix has already been purged from doc
      materialId = materialDict.SetAt(matName, mat);
      tr.AddNewlyCreatedDBObject(mat, true);

      return (materialId, new(Status.SUCCESS, renderMaterial, matName, "Material"));
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      _logger.LogError(ex, "Failed to add a material to the document");
      return (materialId, new(Status.ERROR, renderMaterial, null, null, ex));
    }
  }
}
