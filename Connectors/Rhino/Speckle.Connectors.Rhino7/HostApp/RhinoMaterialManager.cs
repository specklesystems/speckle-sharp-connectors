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
  /// <summary>
  /// A dictionary of (material index, material guid)
  /// </summary>
  private readonly Dictionary<string, SpeckleRenderMaterial> _renderMaterialCache = new();

  /// <summary>
  /// Creates a Speckle Render Material from the provided Rhino material
  /// </summary>
  /// <param name="material"></param>
  /// <returns>The existing Speckle Render Material if this material has been previously created, or the newly created Speckle Render Material</returns>
  public SpeckleRenderMaterial CreateSpeckleRenderMaterial(Material material)
  {
    string materialId = material.Id.ToString();
    if (_renderMaterialCache.ContainsKey(materialId))
    {
      return _renderMaterialCache[materialId];
    }

    // get physically based render material
    Material pbMaterial = material;
    if (!material.IsPhysicallyBased)
    {
      pbMaterial = new();
      pbMaterial.CopyFrom(material);
      pbMaterial.ToPhysicallyBased();
    }

    using RenderMaterial rm = RenderMaterial.FromMaterial(pbMaterial, null);
    Rhino.DocObjects.PhysicallyBasedMaterial pbRenderMaterial = rm.ConvertToPhysicallyBased(
      RenderTexture.TextureGeneration.Allow
    );

    string renderMaterialName = material.Name ?? "default"; // default rhino material has no name
    System.Drawing.Color diffuse = pbRenderMaterial.BaseColor.AsSystemColor();
    System.Drawing.Color emissive = pbRenderMaterial.Emission.AsSystemColor();
    double opacity = pbRenderMaterial.Opacity;

    SpeckleRenderMaterial speckleRenderMaterial =
      new(pbRenderMaterial.Opacity, pbRenderMaterial.Metallic, pbRenderMaterial.Roughness, diffuse, emissive)
      {
        name = renderMaterialName,
        applicationId = materialId
      };

    return speckleRenderMaterial;
  }

  /// <summary>
  /// Determines if a Speckle Render Material has already been created from the input Rhino material
  /// </summary>
  /// <param name="material"></param>
  /// <returns>True if yes, False if no Speckle Render material has been created from the input material</returns>
  public bool Contains(Material material)
  {
    return _renderMaterialCache.TryGetValue(material.Id.ToString(), out SpeckleRenderMaterial _);
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
