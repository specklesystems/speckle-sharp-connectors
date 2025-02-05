using Microsoft.Extensions.Logging;
using Rhino;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Common.Exceptions;
using Material = Rhino.DocObjects.Material;
using RenderMaterial = Rhino.Render.RenderMaterial;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoMaterialBaker
{
  private readonly IConverterSettingsStore<RhinoConversionSettings> _converterSettings;
  private readonly ILogger<RhinoMaterialBaker> _logger;

  public RhinoMaterialBaker(
    IConverterSettingsStore<RhinoConversionSettings> converterSettings,
    ILogger<RhinoMaterialBaker> logger
  )
  {
    _converterSettings = converterSettings;
    _logger = logger;
  }

  /// <summary>
  /// A map keeping track of ids, <b>either layer id or object id</b>, and their render material guid. It's generated from the material proxy list as we bake materials; <see cref="BakeMaterials"/> must be called in advance for this to be populated with the correct data.
  /// </summary>
  public Dictionary<string, RenderMaterial> ObjectIdAndMaterialIndexMap { get; } = new();

  public void BakeMaterials(IReadOnlyCollection<RenderMaterialProxy> speckleRenderMaterialProxies, string baseLayerName)
  {
    var doc = _converterSettings.Current.Document; // POC: too much right now to interface around
    // List<ReceiveConversionResult> conversionResults = new(); // TODO: return this guy

    foreach (var proxy in speckleRenderMaterialProxies)
    {
      var speckleRenderMaterial = proxy.value;

      try
      {
        // POC: Currently we're relying on the render material name for identification if it's coming from speckle and from which model; could we do something else?
        string materialId = speckleRenderMaterial.applicationId ?? speckleRenderMaterial.id.NotNull();
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

        // We are creating a render material and adding it to the render material table because render materials have a guid independent of objects they are applied to.
        // Regular materials and the material table is populated by materials applied to objects: the same material can therefore have multiple entries in the material table if it is applied to multiple objects
        // see: https://discourse.mcneel.com/t/render-material-events/99886/5
        RenderMaterial rhinoRenderMaterial = RenderMaterial.FromMaterial(rhinoMaterial, doc);

        if (doc.RenderMaterials.Add(rhinoRenderMaterial))
        {
          // Create the object <> render material guid map
          foreach (var objectId in proxy.objects)
          {
            ObjectIdAndMaterialIndexMap[objectId] = rhinoRenderMaterial;
          }
        }
        else
        {
          throw new ConversionException("Failed to add a material to the document.");
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to add a material to the document");
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
      try
      {
        if (!material.IsDeleted && material.Name != null && material.Name.Contains(namePrefix))
        {
          currentDoc.Materials.Delete(material);
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to purge a material from the document");
      }
    }
  }
}
