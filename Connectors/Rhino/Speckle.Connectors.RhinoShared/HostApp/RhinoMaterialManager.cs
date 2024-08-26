using Rhino;
using Rhino.DocObjects;
using Rhino.Render;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Material = Rhino.DocObjects.Material;
using PhysicallyBasedMaterial = Rhino.DocObjects.PhysicallyBasedMaterial;
using RenderMaterial = Rhino.Render.RenderMaterial;
using SpeckleRenderMaterial = Speckle.Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Rhino.HostApp;

/// <summary>
/// Utility class managing layer creation and/or extraction from rhino. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoMaterialManager
{
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;

  public RhinoMaterialManager(IConversionContextStack<RhinoDoc, UnitSystem> contextStack)
  {
    _contextStack = contextStack;
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

  /// <summary>
  /// A map keeping track of ids, <b>either layer id or object id</b>, and their material index. It's generated from the material proxy list as we bake materials; <see cref="BakeMaterials"/> must be called in advance for this to be populated with the correct data.
  /// </summary>
  public Dictionary<string, int> ObjectIdAndMaterialIndexMap { get; } = new();

  public void BakeMaterials(List<RenderMaterialProxy> speckleRenderMaterialProxies, string baseLayerName)
  {
    var doc = _contextStack.Current.Document; // POC: too much right now to interface around
    List<ReceiveConversionResult> conversionResults = new(); // TODO: return this guy

    foreach (var proxy in speckleRenderMaterialProxies)
    {
      var speckleRenderMaterial = proxy.value;

      try
      {
        // POC: Currently we're relying on the render material name for identification if it's coming from speckle and from which model; could we do something else?
        string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id;
        string matName = $"{speckleRenderMaterial.name}-({materialId})-{baseLayerName}";
        matName = matName.Replace("[", "").Replace("]", ""); // "Material" doesn't like square brackets if we create from here. Once they created from Rhino UI, all good..
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
          throw new ConversionException("Failed to add a material to the document.");
        }

        // Create the object <> material index map
        foreach (var objectId in proxy.objects)
        {
          ObjectIdAndMaterialIndexMap[objectId] = matIndex;
        }

        conversionResults.Add(new(Status.SUCCESS, speckleRenderMaterial, matName, "Material"));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        conversionResults.Add(new(Status.ERROR, speckleRenderMaterial, null, null, ex));
      }
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
      if (!material.IsDeleted && material.Name != null && material.Name.Contains(namePrefix))
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
