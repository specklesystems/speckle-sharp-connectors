using System.IO;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Conversion;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Converters.Common;
using Speckle.Converters.MicroStation.Services;
using Speckle.Converters.MicroStation.Settings;
using Speckle.Converters.MicroStation.ToSpeckle;
using Speckle.Converters.MicroStation.ToSpeckle.Appearance;
using Speckle.Converters.MicroStation.ToSpeckle.Properties;
using Speckle.Objects.Data;
using Speckle.Objects.Other;
using Speckle.Sdk;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Instances;
using Speckle.Sdk.Models.Proxies;
using Speckle.Sdk.Pipelines.Progress;

namespace Speckle.Connectors.MicroStation.Operations.Send;

/// <summary>
/// Builds the root commit object with the dgnextract-parity structure:
/// <list type="bullet">
/// <item>root <see cref="Collection"/> (design file name) → per-level "layer" collections (DGN
/// levels are CAD layers — ENG-9131; the level tier is flat); reference occurrences get their own
/// sub-collection first, then their layer tier</item>
/// <item><see cref="MicrostationObject"/> per element: displayValue from the recursive dispatcher,
/// properties (level / Item Types EC), name (cell/shared-cell name)</item>
/// <item>shared cells → <c>instanceDefinitionProxies</c> + <see cref="InstanceProxy"/> objects;
/// definition members convert in the definition's local frame</item>
/// <item>appearance → <c>renderMaterialProxies</c> + <c>colorProxies</c>, strictly separate
/// channels; ids are object-level when an element is uniform, per-geometry when mixed</item>
/// </list>
/// </summary>
public class MicroStationRootObjectBuilder(
  DisplayValueExtractor displayValueExtractor,
  PropertiesExtractor propertiesExtractor,
  AppearanceResolver appearanceResolver,
  GeometryMapper geometryMapper,
  IInstanceUnpacker<MicroStationRootObject> instanceUnpacker,
  ISendConversionCache sendConversionCache,
  IConverterSettingsStore<MicroStationConversionSettings> converterSettings,
  ILogger<MicroStationRootObjectBuilder> logger,
  ISdkActivityFactory activityFactory
) : IRootObjectBuilder<MicroStationRootObject>
{
  public Task<RootObjectBuilderResult> Build(
    IReadOnlyList<MicroStationRootObject> objects,
    string projectId,
    IProgress<CardProgress> onOperationProgressed,
    CancellationToken cancellationToken
  )
  {
    using var activity = activityFactory.Start("Build");

    if (objects.Count == 0)
    {
      throw new SpeckleException("No objects to convert.");
    }

    MicroStationConversionSettings settings = converterSettings.Current;
    string docName;
    try
    {
      docName = Path.GetFileName(settings.ActiveModel.GetDgnFile()?.GetFileName() ?? "") is { Length: > 0 } n
        ? n
        : "Unnamed Model";
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      docName = "Unnamed Model";
    }

    var root = new Collection { name = docName, ["units"] = settings.SpeckleUnits };

    // 1 — Unpack shared-cell instances (proxies + definition-member atomic objects).
    UnpackResult<MicroStationRootObject> unpack = instanceUnpacker.UnpackSelection(objects);
    root[ProxyKeys.INSTANCE_DEFINITION] = unpack.InstanceDefinitionProxies;

    // Collections: active-model layer tier at root; each reference occurrence nests its own tier.
    var occurrenceCollections = new Dictionary<string, Collection>();
    var layerCollections = new Dictionary<string, Collection>();

    // Appearance proxies (colour and material channels stay strictly separate — ENG-9130).
    var colorProxies = new Dictionary<int, ColorProxy>();
    var materialProxies = new Dictionary<string, RenderMaterialProxy>();

    var results = new List<SendConversionResult>(unpack.AtomicObjects.Count);
    int processed = 0;

    foreach (MicroStationRootObject obj in unpack.AtomicObjects)
    {
      cancellationToken.ThrowIfCancellationRequested();
      string appId = obj.ApplicationId;
      string sourceType = SourceTypeOf(obj.Element);

      try
      {
        Base? converted = null;
        List<ExtractedGeometry>? extracted = null;

        if (unpack.InstanceProxies.TryGetValue(appId, out InstanceProxy? instanceProxy))
        {
          converted = instanceProxy;
        }
        else if (sendConversionCache.TryGetValue(appId, projectId, out ObjectReference? cached))
        {
          converted = cached;
          // Appearance grouping is conversion-derived; on a cache hit resolve it object-level.
          AttachAppearance(obj.Element, appId, isMeshy: true, colorProxies, materialProxies);
        }
        else
        {
          using IDisposable? occurrenceScope = obj.OccurrenceTransform is BG.DTransform3d t
            ? geometryMapper.PushTransform(t)
            : null;
          using IDisposable? definitionScope = unpack.AtomicDefinitionObjectIds.Contains(appId)
            ? geometryMapper.PushDefinitionFrame()
            : null;

          extracted = displayValueExtractor.Extract(obj.Element);
          if (extracted.Count == 0)
          {
            // Invisible / construction-class / empty elements convert to nothing by design.
            results.Add(new SendConversionResult(Status.WARNING, appId, sourceType));
            continue;
          }

          converted = BuildMicrostationObject(obj, sourceType, extracted, settings.SpeckleUnits);
          RegisterAppearance(appId, extracted, colorProxies, materialProxies);
        }

        Collection target = GetOrAddCollection(root, obj, occurrenceCollections, layerCollections);
        target.elements.Add(converted);
        results.Add(new SendConversionResult(Status.SUCCESS, appId, sourceType, converted));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogError(ex, "Failed to convert element {Id}.", appId);
        results.Add(new SendConversionResult(Status.ERROR, appId, sourceType, null, ex));
      }

      onOperationProgressed.Report(new CardProgress("Converting", (double)++processed / unpack.AtomicObjects.Count));
    }

    if (colorProxies.Count > 0)
    {
      root[ProxyKeys.COLOR] = colorProxies.Values.ToList();
    }
    if (materialProxies.Count > 0)
    {
      root[ProxyKeys.RENDER_MATERIAL] = materialProxies.Values.ToList();
    }

    if (results.Count > 0 && results.All(r => r.Status == Status.ERROR))
    {
      throw new SpeckleException("Failed to convert all objects.");
    }

    return Task.FromResult(new RootObjectBuilderResult(root, results));
  }

  private MicrostationObject BuildMicrostationObject(
    MicroStationRootObject obj,
    string sourceType,
    List<ExtractedGeometry> extracted,
    string units
  )
  {
    string appId = obj.ApplicationId;
    var displayValue = new List<Base>(extracted.Count);
    for (int i = 0; i < extracted.Count; i++)
    {
      Base geometry = extracted[i].Geometry;
      geometry.applicationId ??= $"{appId}-g{i}";
      displayValue.Add(geometry);
    }

    PropertiesResult propertiesResult = propertiesExtractor.Extract(obj.Element);
    Dictionary<string, object?> properties = propertiesResult.Properties;
    if (propertiesResult.IsCivil)
    {
      AddCivilQuantities(displayValue, properties);
    }
    string name = ResolveName(obj.Element) ?? sourceType;

    return new MicrostationObject
    {
      type = sourceType,
      name = name,
      displayValue = displayValue,
      properties = properties,
      units = units,
      applicationId = appId,
    };
  }

  /// <summary>
  /// Civil Quantities (dgnextract's MeshQuantities): sloped area = Σ true 3D triangle areas over
  /// the extracted meshes; planar area = the larger of the upward-/downward-facing XY-projected
  /// sums (a closed civil mesh projects its top and bottom onto the same footprint).
  /// </summary>
  private static void AddCivilQuantities(List<Base> displayValue, Dictionary<string, object?> properties)
  {
    double sloped = 0,
      planarUp = 0,
      planarDown = 0;
    foreach (Base geometry in displayValue)
    {
      if (geometry is not Speckle.Objects.Geometry.Mesh mesh)
      {
        continue;
      }
      List<double> v = mesh.vertices;
      List<int> f = mesh.faces;
      int i = 0;
      while (i < f.Count)
      {
        int n = f[i];
        if (n < 3 || i + n >= f.Count)
        {
          break;
        }
        // fan-triangulate the n-gon
        for (int k = 2; k < n; k++)
        {
          int a = f[i + 1] * 3,
            b = f[i + k] * 3,
            c = f[i + k + 1] * 3;
          if (c + 2 >= v.Count)
          {
            continue;
          }
          double ux = v[b] - v[a],
            uy = v[b + 1] - v[a + 1],
            uz = v[b + 2] - v[a + 2];
          double wx = v[c] - v[a],
            wy = v[c + 1] - v[a + 1],
            wz = v[c + 2] - v[a + 2];
          double cx = uy * wz - uz * wy,
            cy = uz * wx - ux * wz,
            cz = ux * wy - uy * wx;
          sloped += 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
          double projected = 0.5 * cz;
          if (projected >= 0)
          {
            planarUp += projected;
          }
          else
          {
            planarDown -= projected;
          }
        }
        i += n + 1;
      }
    }
    if (sloped <= 0)
    {
      return;
    }
    properties["Civil Quantities"] = new Dictionary<string, object?>
    {
      ["Sloped Area"] = new Dictionary<string, object?> { ["value"] = sloped, ["name"] = "Sloped Area" },
      ["Planar Area"] = new Dictionary<string, object?>
      {
        ["value"] = Math.Max(planarUp, planarDown),
        ["name"] = "Planar Area",
      },
    };
  }

  /// <summary>Element name resolution (dgnextract's resolveName): shared cell → definition name,
  /// cell → cell name, anything else → none.</summary>
  private static string? ResolveName(MgdElement element) =>
    element switch
    {
      MgdElements.SharedCellElement sc when !string.IsNullOrEmpty(sc.CellName) => sc.CellName,
      MgdElements.CellHeaderElement c when !string.IsNullOrEmpty(c.CellName) => c.CellName,
      _ => null,
    };

  private static string SourceTypeOf(MgdElement element)
  {
    string typeName = element.TypeName;
    return string.IsNullOrEmpty(typeName) ? element.ElementType.ToString() : typeName;
  }

  // ── collections ──────────────────────────────────────────────────────────────────────────

  private Collection GetOrAddCollection(
    Collection root,
    MicroStationRootObject obj,
    Dictionary<string, Collection> occurrenceCollections,
    Dictionary<string, Collection> layerCollections
  )
  {
    Collection parent = root;
    if (obj.OccurrenceTag.Length > 0)
    {
      if (!occurrenceCollections.TryGetValue(obj.OccurrenceTag, out Collection? occurrence))
      {
        occurrence = new Collection { name = obj.ContainerLabel, ["occurrenceTag"] = obj.OccurrenceTag };
        occurrenceCollections[obj.OccurrenceTag] = occurrence;
        root.elements.Add(occurrence);
      }
      parent = occurrence;
    }

    var (levelName, _) = PropertiesExtractor.GetLevelInfo(obj.Element);
    if (string.IsNullOrEmpty(levelName))
    {
      return parent;
    }

    string key = $"{obj.OccurrenceTag}|{levelName}";
    if (!layerCollections.TryGetValue(key, out Collection? layer))
    {
      layer = new Collection { name = levelName! };
      layerCollections[key] = layer;
      parent.elements.Add(layer);
    }
    return layer;
  }

  // ── appearance channels ──────────────────────────────────────────────────────────────────

  private void RegisterAppearance(
    string appId,
    List<ExtractedGeometry> extracted,
    Dictionary<int, ColorProxy> colorProxies,
    Dictionary<string, RenderMaterialProxy> materialProxies
  )
  {
    // Uniform appearance → one object-level id (the AutoCAD idiom); mixed → per-geometry ids
    // (dgnextract's geometry-scoped HAS_COLOR / HAS_MATERIAL fidelity for multi-part cells).
    bool uniform = extracted.Select(g => (g.Material?.Key, g.ColorArgb)).Distinct().Count() <= 1 && extracted.Count > 0;

    if (uniform)
    {
      ExtractedGeometry first = extracted[0];
      if (first.Material is ResolvedMaterial material)
      {
        AddToMaterialProxy(material, appId, materialProxies);
      }
      else if (first.ColorArgb is int argb)
      {
        AddToColorProxy(argb, appId, colorProxies);
      }
      return;
    }

    foreach (ExtractedGeometry geometry in extracted)
    {
      string? gid = geometry.Geometry.applicationId;
      if (gid == null)
      {
        continue;
      }
      if (geometry.Material is ResolvedMaterial material)
      {
        AddToMaterialProxy(material, gid, materialProxies);
      }
      else if (geometry.ColorArgb is int argb)
      {
        AddToColorProxy(argb, gid, colorProxies);
      }
    }
  }

  /// <summary>Cache-hit path: geometry-level detail is gone, resolve the element object-level.</summary>
  private void AttachAppearance(
    MgdElement element,
    string appId,
    bool isMeshy,
    Dictionary<int, ColorProxy> colorProxies,
    Dictionary<string, RenderMaterialProxy> materialProxies
  )
  {
    ResolvedMaterial? material = isMeshy ? appearanceResolver.ResolveMaterial(element) : null;
    if (material is ResolvedMaterial m)
    {
      AddToMaterialProxy(m, appId, materialProxies);
      return;
    }
    AddToColorProxy(appearanceResolver.ResolveColorArgb(element), appId, colorProxies);
  }

  private static void AddToColorProxy(int argb, string objectId, Dictionary<int, ColorProxy> colorProxies)
  {
    if (!colorProxies.TryGetValue(argb, out ColorProxy? proxy))
    {
      proxy = new ColorProxy
      {
        value = argb,
        applicationId = $"microstation-color-{argb:X8}",
        name = $"#{argb & 0xFFFFFF:X6}",
        objects = [],
      };
      colorProxies[argb] = proxy;
    }
    if (!proxy.objects.Contains(objectId))
    {
      proxy.objects.Add(objectId);
    }
  }

  private static void AddToMaterialProxy(
    ResolvedMaterial material,
    string objectId,
    Dictionary<string, RenderMaterialProxy> materialProxies
  )
  {
    if (!materialProxies.TryGetValue(material.Key, out RenderMaterialProxy? proxy))
    {
      proxy = new RenderMaterialProxy
      {
        value = new RenderMaterial
        {
          name = material.Name,
          diffuse = material.Argb,
          opacity = material.Opacity,
          applicationId = $"microstation-material-{material.Key}",
        },
        objects = [],
        applicationId = $"microstation-material-{material.Key}",
      };
      materialProxies[material.Key] = proxy;
    }
    if (!proxy.objects.Contains(objectId))
    {
      proxy.objects.Add(objectId);
    }
  }
}
