using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Objects.Other;
using Speckle.Objects.Other.Revit;
using Speckle.Sdk.Common;

namespace Speckle.Converters.RevitShared.ToSpeckle;

public class MeshByMaterialDictionaryToSpeckle
  : ITypedConverter<(Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId), List<SOG.Mesh>>
{
  private readonly ISettingsStore<RevitConversionSettings> _settings;
  private readonly ITypedConverter<DB.Material, (RevitMaterial, RenderMaterial)> _materialConverter;
  private readonly ITypedConverter<List<DB.Mesh>, SOG.Mesh> _meshListConverter;
  private readonly RevitMaterialCacheSingleton _revitMaterialCacheSingleton;

  public MeshByMaterialDictionaryToSpeckle(
    ITypedConverter<DB.Material, (RevitMaterial, RenderMaterial)> materialConverter,
    ITypedConverter<List<DB.Mesh>, SOG.Mesh> meshListConverter,
    ISettingsStore<RevitConversionSettings> settings,
    RevitMaterialCacheSingleton revitMaterialCacheSingleton
  )
  {
    _materialConverter = materialConverter;
    _meshListConverter = meshListConverter;
    _settings = settings;
    _revitMaterialCacheSingleton = revitMaterialCacheSingleton;
  }

  /// <summary>
  /// Converts a dictionary of Revit meshes, where key is MaterialId, into a list of Speckle meshes.
  /// </summary>
  /// <param name="args">A tuple consisting of (1) a dictionary with DB.ElementId keys and List of DB.Mesh values and (2) the root element id (the one generating all the meshes).</param>
  /// <returns>
  /// Returns a list of <see cref="SOG.Mesh"/> objects where each mesh represents one unique material in the input dictionary.
  /// </returns>
  /// <remarks>
  /// Be aware that this method internally creates a new instance of <see cref="SOG.Mesh"/> for each unique material in the input dictionary.
  /// These meshes are created with an initial capacity based on the size of the vertex and face arrays to avoid unnecessary resizing.
  /// Also note that, for each unique material, the method tries to retrieve the related DB.Material from the current document and convert it. If the conversion is successful,
  /// the material is added to the corresponding Speckle mesh. If the conversion fails, the operation simply continues without the material.
  /// TODO: update description
  /// </remarks>
  public List<SOG.Mesh> Convert((Dictionary<DB.ElementId, List<DB.Mesh>> target, DB.ElementId parentElementId) args)
  {
    var result = new List<SOG.Mesh>(args.target.Keys.Count);
    var objectRenderMaterialProxiesMap = _revitMaterialCacheSingleton.ObjectRenderMaterialProxiesMap;

    var materialProxyMap = new Dictionary<string, RenderMaterialProxy>();
    objectRenderMaterialProxiesMap[args.parentElementId.ToString().NotNull()] = materialProxyMap;

    if (args.target.Count == 0)
    {
      return new();
    }

    foreach (var keyValuePair in args.target)
    {
      DB.ElementId materialId = keyValuePair.Key;
      string materialIdString = materialId.ToString().NotNull();
      List<DB.Mesh> meshes = keyValuePair.Value;

      // use the meshlist converter to convert the mesh values into a single speckle mesh
      SOG.Mesh speckleMesh = _meshListConverter.Convert(meshes);
      speckleMesh.applicationId = Guid.NewGuid().ToString(); // NOTE: as we are composing meshes out of multiple ones for the same material, we need to generate our own application id. c'est la vie.

      // get the render material if any
      if (_settings.Current.Document.GetElement(materialId) is DB.Material material)
      {
        (RevitMaterial _, RenderMaterial convertedRenderMaterial) = _materialConverter.Convert(material);

        if (!materialProxyMap.TryGetValue(materialIdString, out RenderMaterialProxy? renderMaterialProxy))
        {
          renderMaterialProxy = new RenderMaterialProxy()
          {
            value = convertedRenderMaterial,
            applicationId = materialId.ToString(),
            objects = []
          };
          materialProxyMap[materialIdString] = renderMaterialProxy;
        }

        renderMaterialProxy.objects.Add(speckleMesh.applicationId);
      }

      result.Add(speckleMesh);
    }

    return result;
  }
}
