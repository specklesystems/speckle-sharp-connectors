using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Objects.Other;
using Speckle.Sdk;
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
  /// Maps object/layer ids to their legacy material index.
  /// Used by RhinoHostObjectBuilder.BakeObject, RhinoLayerBaker and RhinoInstanceBaker
  /// for reliable synchronous material assignment without needing RenderContent.FromId,
  /// which returns null when called off the main thread (CNX-3311).
  /// </summary>
  public Dictionary<string, int> ObjectIdAndMaterialIndexMap { get; } = [];

  public void BakeMaterials(IReadOnlyCollection<RenderMaterialProxy> speckleRenderMaterialProxies)
  {
    var doc = _converterSettings.Current.Document; // POC: too much right now to interface around
    // List<ReceiveConversionResult> conversionResults = new(); // TODO: return this guy

    foreach (var proxy in speckleRenderMaterialProxies)
    {
      var speckleRenderMaterial = proxy.value;

      try
      {
        // POC: Currently we're relying on the render material name for identification if it's coming from speckle and from which model; could we do something else?
        string matName = speckleRenderMaterial.name;
        matName = matName.Replace("[", "").Replace("]", ""); // "Material" doesn't like square brackets if we create from here. Once they created from Rhino UI, all good..

        // Check if material with this name already exists in the document
        var existingRenderMaterial = doc.RenderMaterials.FirstOrDefault(m => m.Name == matName);
        Guid materialGuid;
        int materialIndex;

        if (existingRenderMaterial == null)
        {
          Color diffuse = Color.FromArgb(speckleRenderMaterial.diffuse);
          Color emissive = Color.FromArgb(speckleRenderMaterial.emissive);
          double transparency = 1 - speckleRenderMaterial.opacity;

          Material rhinoMaterial = new()
          {
            Name = matName,
            DiffuseColor = diffuse,
            EmissionColor = emissive,
            Transparency = transparency,
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

          // create RenderMaterial wrapper (CNX-2896)
          // From my understanding, this internally manages the legacy Material table entry
          var renderMaterial = RenderMaterial.CreateBasicMaterial(rhinoMaterial, doc);
          doc.RenderMaterials.Add(renderMaterial);
          materialGuid = renderMaterial.Id;

          // Resolve the legacy index by name immediately after adding (CNX-3311).
          // We use the name directly rather than RenderContent.FromId because the RDK
          // legacy table sync is not guaranteed to be immediate for all material types,
          // causing FromId to return null. Since we just added this material with matName,
          // doc.Materials.Find is guaranteed to find it.
          materialIndex = doc.Materials.Find(matName, ignoreDeletedMaterials: true);
        }
        else
        {
          materialGuid = existingRenderMaterial.Id;
          materialIndex = doc.Materials.Find(matName, ignoreDeletedMaterials: true);
        }

        if (materialGuid == Guid.Empty)
        {
          throw new ConversionException($"Failed to create or retrieve RenderMaterial Guid for: '{matName}'");
        }

        if (materialIndex == -1)
        {
          throw new ConversionException($"Failed to resolve legacy material index for: '{matName}'");
        }

        // map object ID to material index
        foreach (var objectId in proxy.objects)
        {
          ObjectIdAndMaterialIndexMap[objectId] = materialIndex;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to add a modern RenderMaterial to the document");
      }
    }
  }
}
