using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.GraphicsInterface;
using Speckle.Connectors.Autocad.HostApp.Extensions;
using Speckle.Connectors.Autocad.Operations.Send;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Material = Autodesk.AutoCAD.DatabaseServices.Material;
using RenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Autocad.HostApp;

/// <summary>
/// Expects to be a scoped dependency for a given operation and helps with layer creation and cleanup.
/// </summary>
public class AutocadMaterialManager
{
  private readonly AutocadContext _autocadContext;

  // POC: Will be addressed to move it into AutocadContext!
  private Document Doc => Application.DocumentManager.MdiActiveDocument;

  public Dictionary<string, ObjectId> ObjectMaterialsIdMap { get; } = new();

  public AutocadMaterialManager(AutocadContext autocadContext)
  {
    _autocadContext = autocadContext;
  }

  private RenderMaterialProxy ConvertMaterialToRenderMaterialProxy(Material material, string id)
  {
    EntityColor diffuseColor = material.Diffuse.Color.Color;
    System.Drawing.Color diffuse = System.Drawing.Color.FromArgb(
      diffuseColor.Red,
      diffuseColor.Green,
      diffuseColor.Blue
    );

    string name = material.Name;
    double opacity = material.Opacity.Percentage;

    RenderMaterial renderMaterial = new(opacity: opacity, diffuse: diffuse) { name = name, applicationId = id };

    // Add additional properties
    renderMaterial["ior"] = material.Refraction.Index;
    renderMaterial["reflectivity"] = material.Reflectivity;

    return new(renderMaterial, new()) { applicationId = id };
  }

  /// <summary>
  /// Iterates through a given set of autocad objects and collects their materials. Note: expects objects to be "atomic", and extracted out of their instances already.
  /// </summary>
  /// <param name="unpackedAutocadObjects"></param>
  /// <param name="layers"></param>
  /// <returns></returns>
  public List<RenderMaterialProxy> UnpackMaterials(
    List<AutocadRootObject> unpackedAutocadObjects,
    List<LayerTableRecord> layers
  )
  {
    Dictionary<string, RenderMaterialProxy> materialProxies = new();
    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();

    // Stage 1: unpack materials from objects
    foreach (AutocadRootObject rootObj in unpackedAutocadObjects)
    {
      Entity entity = rootObj.Root;

      // skip inherited materials
      if (entity.Material == "ByLayer" || entity.Material == "ByBlock")
      {
        continue;
      }

      if (transaction.GetObject(entity.MaterialId, OpenMode.ForRead) is Material material)
      {
        string materialId = material.Handle.ToString();
        if (materialProxies.TryGetValue(materialId, out RenderMaterialProxy? value))
        {
          value.objects.Add(rootObj.ApplicationId);
        }
        else
        {
          RenderMaterialProxy materialProxy = ConvertMaterialToRenderMaterialProxy(material, materialId);
          materialProxy.objects.Add(rootObj.ApplicationId);
          materialProxies[materialId] = materialProxy;
        }
      }
    }

    // Stage 2: make sure we collect layer colors as well
    foreach (LayerTableRecord layer in layers)
    {
      if (transaction.GetObject(layer.MaterialId, OpenMode.ForRead) is Material material)
      {
        string materialId = material.Handle.ToString();
        string layerId = layer.GetSpeckleApplicationId(); // Do not use handle directly, see note in the 'GetSpeckleApplicationId' method
        if (materialProxies.TryGetValue(materialId, out RenderMaterialProxy? value))
        {
          value.objects.Add(layerId);
        }
        else
        {
          RenderMaterialProxy materialProxy = ConvertMaterialToRenderMaterialProxy(material, materialId);
          materialProxy.objects.Add(layerId);
          materialProxies[materialId] = materialProxy;
        }
      }
    }

    transaction.Commit();
    return materialProxies.Values.ToList();
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
      string renderMaterialId = renderMaterial.applicationId ?? renderMaterial.id;
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
      return (materialId, new(Status.ERROR, renderMaterial, null, null, ex));
    }
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
        if (entry.Key.Contains(namePrefix))
        {
          materialDict.Remove(entry.Value);
        }
      }
    }
    transaction.Commit();
  }

  public List<ReceiveConversionResult> ParseAndBakeRenderMaterials(
    List<RenderMaterialProxy> materialProxies,
    string baseLayerPrefix,
    Action<string, double?>? onOperationProgressed
  )
  {
    List<ReceiveConversionResult> results = new();
    Dictionary<string, string> objectRenderMaterialsIdMap = new();

    using var transaction = Application.DocumentManager.CurrentDocument.Database.TransactionManager.StartTransaction();
    var materialDict = transaction.GetObject(Doc.Database.MaterialDictionaryId, OpenMode.ForWrite) as DBDictionary;

    if (materialDict == null)
    {
      // POC: we should report failed conversion here if material dict is not accessible, but it is not linked to a Base source
      transaction.Commit();
      return results;
    }

    var count = 0;
    foreach (RenderMaterialProxy materialProxy in materialProxies)
    {
      onOperationProgressed?.Invoke("Converting render materials", (double)++count / materialProxies.Count);

      // bake render material
      RenderMaterial renderMaterial = materialProxy.value;
      string renderMaterialId = renderMaterial.applicationId ?? renderMaterial.id;
      ObjectId materialId = ObjectId.Null;

      if (!ObjectMaterialsIdMap.TryGetValue(renderMaterialId, out materialId))
      {
        (materialId, ReceiveConversionResult result) = BakeMaterial(
          renderMaterial,
          baseLayerPrefix,
          materialDict,
          transaction
        );

        results.Add(result);
      }
      else
      {
        // POC: this shouldn't happen, but will if there are render materials with the same applicationID
        results.Add(
          new(
            Status.ERROR,
            renderMaterial,
            exception: new ArgumentException("Another render material of the same id has already been created.")
          )
        );
      }

      if (materialId == ObjectId.Null)
      {
        results.Add(
          new(
            Status.ERROR,
            renderMaterial,
            exception: new InvalidOperationException("Render material failed to be added to document.")
          )
        );

        continue;
      }

      // parse render material object ids
      foreach (string objectId in materialProxy.objects)
      {
        ObjectMaterialsIdMap[objectId] = materialId;
      }
    }

    transaction.Commit();
    return results;
  }
}
