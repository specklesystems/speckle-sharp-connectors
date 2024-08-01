using Objects.Other;
using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using BakeResult = Speckle.Connectors.Utils.RenderMaterials.BakeResult;
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

    string renderMaterialName = renderMaterial.Name ?? "default"; // default rhino material has no name
    Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    Color emissive = pbRenderMaterial.Emission.AsSystemColor();
    SpeckleRenderMaterial speckleRenderMaterial =
      new(pbRenderMaterial.Opacity, pbRenderMaterial.Metallic, pbRenderMaterial.Roughness, diffuse, emissive)
      {
        name = renderMaterialName,
        applicationId = renderMaterial.Id.ToString()
      };

    return speckleRenderMaterial;
  }

  public BakeResult BakeMaterials(
    List<SpeckleRenderMaterial> speckleRenderMaterials,
    string baseLayerName,
    Action<string, double?>? onOperationProgressed
  )
  {
    var doc = RhinoDoc.ActiveDoc; // POC: too much right now to interface around

    // Keeps track of the incoming SpeckleRenderMaterial application Id and the index of the corresponding Rhino Material in the doc material table
    Dictionary<string, int> materialIdAndIndexMap = new();

    int count = 0;
    List<ReceiveConversionResult> conversionResults = new();
    foreach (SpeckleRenderMaterial speckleRenderMaterial in speckleRenderMaterials)
    {
      onOperationProgressed?.Invoke("Converting render materials", (double)++count / speckleRenderMaterials.Count);
      try
      {
        // POC: Currently we're relying on the render material name for identification if it's coming from speckle and from which model; could we do something else?
        // POC: we should assume render materials all have application ids?
        string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id;
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

        int matIndex = doc.Materials.Add(rhinoMaterial);

        // POC: check on matIndex -1, means we haven't created anything - this is most likely an recoverable error at this stage
        if (matIndex == -1)
        {
          throw new ConversionException("Failed to add a material to the document.");
        }

        materialIdAndIndexMap[materialId] = matIndex;

        conversionResults.Add(new(Status.SUCCESS, speckleRenderMaterial, matName, "Material"));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, speckleRenderMaterial, null, null, ex));
      }
    }

    return new(materialIdAndIndexMap, conversionResults);
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
          myMaterial = ConvertRenderMaterialToSpeckle(ConvertMaterialToRenderMaterial(rhinoMaterial));
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
