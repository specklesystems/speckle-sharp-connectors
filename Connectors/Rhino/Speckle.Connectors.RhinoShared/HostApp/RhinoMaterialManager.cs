using System.Diagnostics.CodeAnalysis;
using Objects.Other;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using Speckle.Logging;
using Material = Rhino.DocObjects.Material;
using PhysicallyBasedMaterial = Rhino.DocObjects.PhysicallyBasedMaterial;
using RenderMaterial = Rhino.Render.RenderMaterial;
using SpeckleRenderMaterial = Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation and/or extraction from rhino. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoMaterialManager
{
  /// <summary>
  /// Keeps track of the object id to render material index
  /// </summary>
  public Dictionary<string, int> ObjectMaterialsIdMap { get; } = new();

  [SuppressMessage("Performance", "CA1864:Prefer the \'IDictionary.TryAdd(TKey, TValue)\' method")]
  public List<ReceiveConversionResult> BakeRenderMaterials(
    List<RenderMaterialProxy> materialProxies,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    List<ReceiveConversionResult> conversionResults = new();
    using var _ = SpeckleActivityFactory.Start("BakeRenderMaterials");

    var doc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    // keeps track of the material id to created index in the materials table
    Dictionary<string, int> materialsIdMap = new();
    int count = 0;
    foreach (RenderMaterialProxy materialProxy in materialProxies)
    {
      onOperationProgressed?.Invoke("Converting render materials", (double)++count / materialProxies.Count);
      string materialId = materialProxy.value.applicationId ?? materialProxy.value.id;

      // bake the render material
      ReceiveConversionResult result = BakeRenderMaterial(
        materialProxy.value,
        materialId,
        materialsIdMap,
        baseLayerName,
        doc
      );

      conversionResults.Add(result);

      // process render material proxy object ids
      foreach (string objectId in materialProxy.objects)
      {
        if (materialsIdMap.TryGetValue(materialId, out int materialIndex))
        {
          if (!ObjectMaterialsIdMap.ContainsKey(objectId))
          {
            ObjectMaterialsIdMap.Add(objectId, materialIndex);
          }
        }
      }
    }

    return conversionResults;
  }

  // converts a rhino material to a rhino render material
  private RenderMaterial ConvertMaterialToRenderMaterial(Material material)
  {
    // get physically based render material
    Material pbMaterial = material;
    if (!material.IsPhysicallyBased)
    {
      pbMaterial = new();
      pbMaterial.CopyFrom(material);
      pbMaterial.ToPhysicallyBased();
    }

    return RenderMaterial.FromMaterial(pbMaterial, null);
  }

  private SpeckleRenderMaterial ConvertRenderMaterialToSpeckle(RenderMaterial renderMaterial)
  {
    PhysicallyBasedMaterial pbRenderMaterial = renderMaterial.ConvertToPhysicallyBased(
      RenderTexture.TextureGeneration.Allow
    );

    // get opacity
    // POC: pbr will return opacity = 0 for these because they are not pbr materials, they are transparent materials with IOR. Currently hardcoding 0.2 value in lieu of proper type support in rhino.
    double opacity = (renderMaterial.SmellsLikeGem || renderMaterial.SmellsLikeGlass) ? 0.2 : pbRenderMaterial.Opacity;

    string renderMaterialName = renderMaterial.Name ?? "default"; // default rhino material has no name
    Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    Color emissive = renderMaterial.TypeName.Equals("Emission")
      ? pbRenderMaterial.Material.EmissionColor
      : pbRenderMaterial.Emission.AsSystemColor(); // pbRenderMaterial.emission gives wrong color for emission materials, and material.emissioncolor gives the wrong value for most others *shrug*

    SpeckleRenderMaterial speckleRenderMaterial =
      new(opacity, pbRenderMaterial.Metallic, pbRenderMaterial.Roughness, diffuse, emissive)
      {
        name = renderMaterialName,
        applicationId = renderMaterial.Id.ToString()
      };

    // add additional dynamic props for rhino material receive
    speckleRenderMaterial["typeName"] = renderMaterial.TypeName;
    speckleRenderMaterial["ior"] = pbRenderMaterial.Material.IndexOfRefraction;
    speckleRenderMaterial["shine"] = pbRenderMaterial.Material.Shine;

    return speckleRenderMaterial;
  }

  private ReceiveConversionResult BakeRenderMaterial(
    SpeckleRenderMaterial speckleRenderMaterial,
    string materialId,
    Dictionary<string, int> materialsIdMap,
    string baseLayerName,
    RhinoDoc doc
  )
  {
    // We shouldn't be processing render materials with the same id, report error if duplicate ids are found
    if (materialsIdMap.ContainsKey(materialId))
    {
      return new(
        Status.ERROR,
        speckleRenderMaterial,
        null,
        null,
        new ConversionException($"Already converted a render material with the same id: {materialId}")
      );
    }

    try
    {
      // POC: Currently we're relying on the render material name for identification if it's coming from speckle and from which model; could we do something else?
      // POC: we should assume render materials all have application ids?
      string matName = $"{speckleRenderMaterial.name}-({materialId})-{baseLayerName}";
      Color diffuse = Color.FromArgb(speckleRenderMaterial.diffuse);
      Color emissive = Color.FromArgb(speckleRenderMaterial.emissive);
      double transparency = 1 - speckleRenderMaterial.opacity;

      Material rhinoMaterial =
        new()
        {
          Name = matName,
          DiffuseColor = diffuse,
          EmissionColor = emissive,
          Transparency = transparency
        };

      // try to get additional properties
      if (speckleRenderMaterial["ior"] is double ior)
      {
        rhinoMaterial.IndexOfRefraction = ior;
      }
      if (speckleRenderMaterial["shine"] is double shine)
      {
        rhinoMaterial.Shine = shine;
      }

      int matIndex = doc.Materials.Add(rhinoMaterial);

      // POC: check on matIndex -1, means we haven't created anything - this is most likely an recoverable error at this stage
      if (matIndex == -1)
      {
        return new(
          Status.ERROR,
          speckleRenderMaterial,
          null,
          null,
          new ConversionException("Failed to add a material to the document.")
        );
      }

      materialsIdMap[materialId] = matIndex;
      return new(Status.SUCCESS, speckleRenderMaterial, matName, "Material");
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return new(Status.ERROR, speckleRenderMaterial, null, null, ex);
    }
  }

  /// <summary>
  /// Removes all materials with a name starting with <paramref name="namePrefix"/> from the active document
  /// </summary>
  /// <param name="namePrefix"></param>
  public void PurgeMaterials(string namePrefix)
  {
    var currentDoc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around
    foreach (Material material in currentDoc.Materials)
    {
      if (!material.IsDeleted && material.Name.Contains(namePrefix))
      {
        currentDoc.Materials.Delete(material);
      }
    }
  }

  public List<RenderMaterialProxy> UnpackRenderMaterial(List<RhinoObject> atomicObjects)
  {
    Dictionary<string, RenderMaterialProxy> renderMaterialProxies = new();
    Dictionary<string, Layer> usedLayerMap = new();

    // Stage 1: unpack materials from objects, and collect their uniqe layers in the process
    foreach (RhinoObject rhinoObject in atomicObjects)
    {
      var layer = RhinoDoc.ActiveDoc.Layers[rhinoObject.Attributes.LayerIndex];
      usedLayerMap[layer.Id.ToString()] = layer;

      if (rhinoObject.Attributes.MaterialSource != ObjectMaterialSource.MaterialFromObject)
      {
        continue; // TODO: will not catch layer materials
      }

      var rhinoRenderMaterial = rhinoObject.GetRenderMaterial(true);
      var rhinoMaterial = rhinoObject.GetMaterial(true);
      var rhinoMaterialId = rhinoRenderMaterial?.Id.ToString() ?? rhinoMaterial?.Id.ToString();

      if (rhinoMaterialId == null)
      {
        continue;
      }

      if (renderMaterialProxies.TryGetValue(rhinoMaterialId, out RenderMaterialProxy? value))
      {
        value.objects.Add(rhinoObject.Id.ToString());
      }
      else
      {
        // TY Rhino api for being a bit confused about materials 💖
        SpeckleRenderMaterial? myMaterial = null;
        if (rhinoRenderMaterial is not null)
        {
          myMaterial = ConvertRenderMaterialToSpeckle(rhinoRenderMaterial);
        }
        else if (rhinoMaterial is not null)
        {
          RenderMaterial convertedRender = ConvertMaterialToRenderMaterial(rhinoMaterial);
          myMaterial = ConvertRenderMaterialToSpeckle(convertedRender);
        }

        if (myMaterial is not null)
        {
          renderMaterialProxies[rhinoMaterialId] = new RenderMaterialProxy()
          {
            value = myMaterial,
            objects = [rhinoObject.Id.ToString()]
          };
        }
      }
    }

    // Stage 2: make sure we collect layer materials as well
    foreach (var layer in usedLayerMap.Values)
    {
      var material = layer.RenderMaterial;
      if (material is null)
      {
        continue;
      }

      if (renderMaterialProxies.TryGetValue(material.Id.ToString(), out RenderMaterialProxy? value))
      {
        value.objects.Add(layer.Id.ToString());
      }
      else
      {
        renderMaterialProxies[material.Id.ToString()] = new RenderMaterialProxy()
        {
          value = ConvertRenderMaterialToSpeckle(material),
          objects = [layer.Id.ToString()]
        };
      }
    }

    return renderMaterialProxies.Values.ToList();
  }
}
