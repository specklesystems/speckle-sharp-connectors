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
  /// A map keeping track of ids, either layer id or object id, and their Render Material Guid.
  /// It's generated from the material proxy list as we <see cref="BakeMaterials"/>.
  /// </summary>
  public Dictionary<string, Guid> ObjectIdAndMaterialIdMap { get; } = [];

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
          var renderMaterial = RenderMaterial.CreateBasicMaterial(rhinoMaterial, doc);

          // add to RenderMaterial table. From my understanding, this internally manages the legacy Material table entry
          doc.RenderMaterials.Add(renderMaterial);
          materialGuid = renderMaterial.Id;
        }
        else
        {
          materialGuid = existingRenderMaterial.Id;
        }

        if (materialGuid == Guid.Empty)
        {
          throw new ConversionException($"Failed to create or retrieve RenderMaterial Guid for: '{matName}'");
        }

        // map object ID to Material Guid
        foreach (var objectId in proxy.objects)
        {
          ObjectIdAndMaterialIdMap[objectId] = materialGuid;
        }
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to add a modern RenderMaterial to the document");
      }
    }
  }
}
