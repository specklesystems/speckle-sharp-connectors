using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MeshByMaterialDictionaryToSpeckle
  : ITypedConverter<
    (Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId, bool makeTransparent),
    List<SOG.Mesh>
  >
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly ITypedConverter<DB.Material, RenderMaterial> _speckleRenderMaterialConverter;
  private readonly ITypedConverter<List<DB.Mesh>, SOG.Mesh> _meshListConverter;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;

  private readonly RenderMaterial _transparentMaterial =
    new()
    {
      name = "Transparent",
      diffuse = System.Drawing.Color.Transparent.ToArgb(),
      opacity = 0.3,
      applicationId = "material_Transparent"
    };

  public MeshByMaterialDictionaryToSpeckle(
    ITypedConverter<List<DB.Mesh>, SOG.Mesh> meshListConverter,
    IConverterSettingsStore<RevitConversionSettings> converterSettings,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    ITypedConverter<DB.Material, RenderMaterial> speckleRenderMaterialConverter
  )
  {
    _meshListConverter = meshListConverter;
    _converterSettings = converterSettings;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _speckleRenderMaterialConverter = speckleRenderMaterialConverter;
  }

  /// <summary>
  /// Converts a dictionary of Revit meshes, where key is MaterialId, into a list of Speckle meshes.
  /// </summary>
  /// <returns>
  /// Returns a list of <see cref="SOG.Mesh"/> objects where each mesh represents one unique material in the input dictionary.
  /// </returns>
  /// <remarks>
  /// <para>
  /// This method creates a new instance of <see cref="SOG.Mesh"/> for each unique material in the input dictionary.
  /// </para>
  /// <para>
  /// For each unique material, the method retrieves the related DB.Material from the current document and converts it to a <see cref="RenderMaterial"/>.
  /// Material proxies are created but their objects lists are NOT populated at this stage. The mesh-to-material relationship is stored
  /// in <see cref="RevitToSpeckleCacheSingleton.MeshToMaterialMap"/> for later population during display value processing.
  /// </para>
  /// <para>
  /// Deferred population of the object list to ensure that instance geometry references the definition mesh ID in material proxies,
  /// rather than individual instance mesh IDs. We can only do this later, because proxification hasn't happened yet.
  /// </para>
  /// </remarks>
  public List<SOG.Mesh> Convert(
    (Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId, bool makeTransparent) args
  )
  {
    List<SOG.Mesh> result = new(args.target.Keys.Count);
    var objectRenderMaterialProxiesMap = _revitToSpeckleCacheSingleton.ObjectRenderMaterialProxiesMap;
    var materialProxyMap = new Dictionary<string, RenderMaterialProxy>();
    var key = args.parentElementId.ToString().NotNull();

    if (objectRenderMaterialProxiesMap.TryGetValue(key, out var cachedMaterialProxy))
    {
      materialProxyMap = cachedMaterialProxy;
    }
    else
    {
      objectRenderMaterialProxiesMap[key] = materialProxyMap;
    }

    if (args.target.Count == 0)
    {
      return new();
    }

    foreach (var keyValuePair in args.target)
    {
      DB.ElementId materialId = keyValuePair.Key;
      string materialIdString = args.makeTransparent
        ? _transparentMaterial.applicationId.NotNull()
        : materialId.ToString().NotNull();
      List<DB.Mesh> meshes = keyValuePair.Value;

      SOG.Mesh speckleMesh = _meshListConverter.Convert(meshes);
      speckleMesh.applicationId = Guid.NewGuid().ToString();

      // store mesh-to-material mapping
      if (!_revitToSpeckleCacheSingleton.MeshToMaterialMap.TryGetValue(key, out var meshMatMap))
      {
        meshMatMap = new Dictionary<string, string>();
        _revitToSpeckleCacheSingleton.MeshToMaterialMap[key] = meshMatMap;
      }
      meshMatMap[speckleMesh.applicationId.NotNull()] = materialIdString;

      RenderMaterial? renderMaterial = args.makeTransparent
        ? _transparentMaterial
        : _converterSettings.Current.Document.GetElement(materialId) is DB.Material material
          ? _speckleRenderMaterialConverter.Convert(material)
          : null;

      // Create proxy but DON'T populate objects list yet
      if (renderMaterial is not null)
      {
        if (!materialProxyMap.ContainsKey(materialIdString))
        {
          RenderMaterialProxy? renderMaterialProxy =
            new()
            {
              value = renderMaterial,
              applicationId = materialId.ToString(),
              objects = []
            };
          materialProxyMap[materialIdString] = renderMaterialProxy;
        }
      }

      result.Add(speckleMesh);
    }

    return result;
  }
}
