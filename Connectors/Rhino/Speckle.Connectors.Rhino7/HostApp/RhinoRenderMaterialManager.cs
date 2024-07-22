using Rhino.Render;
using Material = Rhino.DocObjects.Material;
using RenderMaterial = Rhino.Render.RenderMaterial;
using SpeckleRenderMaterial = Objects.Other.RenderMaterial;

namespace Speckle.Connectors.Rhino7.HostApp;

/// <summary>
/// Utility class managing layer creation and/or extraction from rhino. Expects to be a scoped dependency per send or receive operation.
/// </summary>
public class RhinoRenderMaterialManager
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
    return _renderMaterialCache.ContainsKey(material.Id.ToString());
  }
}
