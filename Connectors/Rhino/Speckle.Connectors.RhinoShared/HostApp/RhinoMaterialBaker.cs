﻿using Microsoft.Extensions.Logging;
using Rhino;
using Speckle.Converters.Common;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Material = Rhino.DocObjects.Material;

namespace Speckle.Connectors.Rhino.HostApp;

public class RhinoMaterialBaker
{
  private readonly IConversionContextStack<RhinoDoc, UnitSystem> _contextStack;
  private readonly ILogger<RhinoMaterialBaker> _logger;

  public RhinoMaterialBaker(
    IConversionContextStack<RhinoDoc, UnitSystem> contextStack,
    ILogger<RhinoMaterialBaker> logger
  )
  {
    _contextStack = contextStack;
    _logger = logger;
  }

  /// <summary>
  /// A map keeping track of ids, <b>either layer id or object id</b>, and their material index. It's generated from the material proxy list as we bake materials; <see cref="BakeMaterials"/> must be called in advance for this to be populated with the correct data.
  /// </summary>
  public Dictionary<string, int> ObjectIdAndMaterialIndexMap { get; } = new();

  public void BakeMaterials(List<RenderMaterialProxy> speckleRenderMaterialProxies, string baseLayerName)
  {
    var doc = _contextStack.Current.Document; // POC: too much right now to interface around
    // List<ReceiveConversionResult> conversionResults = new(); // TODO: return this guy

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
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        _logger.LogError(ex, "Failed to add a material to the document."); // TODO: Check with Jedd!
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
        _logger.LogError(ex, "Failed to purge a material from the document."); // TODO: Check with Jedd!
      }
    }
  }
}
