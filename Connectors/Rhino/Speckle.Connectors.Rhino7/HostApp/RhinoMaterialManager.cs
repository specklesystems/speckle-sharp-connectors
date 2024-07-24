using Rhino;
using Rhino.Render;
using Speckle.Connectors.Utils.Conversion;
using Speckle.Core.Kits;
using Speckle.Core.Logging;
using BakeResult = Speckle.Connectors.Utils.RenderMaterials.BakeResult;
using Material = Rhino.DocObjects.Material;
using RenderMaterial = Rhino.Render.RenderMaterial;
using SpeckleRenderMaterial = Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Rhino7.HostApp;

/// <summary>
/// Utility class managing layer creation and/or extraction from rhino. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoMaterialManager
{
  public Dictionary<string, SpeckleRenderMaterial> SpeckleRenderMaterials { get; } = [];

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

  private SpeckleRenderMaterial ConvertRenderMaterialToSpeckle(RenderMaterial renderMaterial, string id)
  {
    Rhino.DocObjects.PhysicallyBasedMaterial pbRenderMaterial = renderMaterial.ConvertToPhysicallyBased(
      RenderTexture.TextureGeneration.Allow
    );

    string renderMaterialName = renderMaterial.Name ?? "default"; // default rhino material has no name
    System.Drawing.Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    System.Drawing.Color emissive = pbRenderMaterial.Emission.AsSystemColor();

    SpeckleRenderMaterial speckleRenderMaterial =
      new(pbRenderMaterial.Opacity, pbRenderMaterial.Metallic, pbRenderMaterial.Roughness, diffuse, emissive)
      {
        name = renderMaterialName,
        applicationId = id
      };

    return speckleRenderMaterial;
  }

  /// <summary>
  /// Creates a Speckle Render Material from the provided Rhino material, if not already existing in <see cref="SpeckleRenderMaterials"/>
  /// </summary>
  /// <param name="material"></param>
  public void CreateSpeckleRenderMaterial(Material material)
  {
    string materialId = material.Id.ToString();
    if (SpeckleRenderMaterials.TryGetValue(materialId, out SpeckleRenderMaterial _))
    {
      return;
    }

    using RenderMaterial renderMaterial = ConvertMaterialToRenderMaterial(material);
    SpeckleRenderMaterial speckleRenderMaterial = ConvertRenderMaterialToSpeckle(renderMaterial, materialId);
    SpeckleRenderMaterials.Add(materialId, speckleRenderMaterial);
  }

  /// <summary>
  /// Creates a Speckle Render Material from the provided Rhino render material, if not already existing in <see cref="SpeckleRenderMaterials"/>
  /// </summary>
  /// <param name="renderMaterial"></param>
  public void CreateSpeckleRenderMaterial(RenderMaterial renderMaterial)
  {
    string materialId = renderMaterial.Id.ToString();
    if (SpeckleRenderMaterials.TryGetValue(materialId, out SpeckleRenderMaterial _))
    {
      return;
    }

    SpeckleRenderMaterial speckleRenderMaterial = ConvertRenderMaterialToSpeckle(renderMaterial, materialId);
    SpeckleRenderMaterials.Add(materialId, speckleRenderMaterial);
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
        string matName = $"{speckleRenderMaterial.name}-({speckleRenderMaterial.applicationId})-{baseLayerName}";
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

        if (speckleRenderMaterial.applicationId != null)
        {
          materialIdAndIndexMap[speckleRenderMaterial.applicationId] = matIndex;
        }

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
}
