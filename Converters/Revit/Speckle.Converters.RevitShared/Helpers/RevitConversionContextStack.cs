using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Objects.Other;

namespace Speckle.Converters.RevitShared.Helpers;

[System.Diagnostics.CodeAnalysis.SuppressMessage(
  "Naming",
  "CA1711:Identifiers should not have incorrect suffix",
  Justification = "See base class justification"
)]
// POC: so this should *probably* be Document and NOT UI.UIDocument, the former is Conversion centric
// and the latter is more for connector
public class RevitConversionContextStack : ConversionContextStack<Document, ForgeTypeId>, IRevitConversionContextStack
{
  /// <summary>
  /// Persistent cache (across conversions) for all generated render material proxies. Note this cache stores a list of render material proxies per element id.
  /// </summary>
  public RenderMaterialProxyCacheSingleton RenderMaterialProxyCache { get; }

  /// <summary>
  /// Keeps track of all converted elements and any resulting subelement ids. For example, if sending a curtain wall, this will contain the original wall id, as well as the ids of all the mullions/panels. Stacked wall component ids will also be stored in here.
  /// </summary>
  public List<ElementId> ConvertedElementsAndSubelementIds { get; } = new();

  public const double TOLERANCE = 0.0164042; // 5mm in ft

  public RevitConversionContextStack(
    RevitContext context,
    IHostToSpeckleUnitConverter<ForgeTypeId> unitConverter,
    RenderMaterialProxyCacheSingleton renderMaterialProxyCache
  )
    : base(
      // POC: we probably should not get here without a valid document
      // so should this perpetuate or do we assume this is valid?
      // relting on the context.UIApplication?.ActiveUIDocument is not right
      // this should be some IActiveDocument I suspect?
      context.UIApplication?.ActiveUIDocument?.Document
        ?? throw new SpeckleConversionException("Active UI document could not be determined"),
      context.UIApplication.ActiveUIDocument.Document.GetUnits().GetFormatOptions(SpecTypeId.Length).GetUnitTypeId(),
      unitConverter
    )
  {
    RenderMaterialProxyCache = renderMaterialProxyCache;
  }
}

/// <summary>
/// singleton; should persist across units of work
/// TODO: description
/// TODO: move to appropriate location
/// </summary>
public class RenderMaterialProxyCacheSingleton
{
  /// <summary>
  /// map(object id, ( map (materialId, proxy) ) )
  /// a per object map of material proxies. not the best way???
  /// </summary>
  public Dictionary<string, Dictionary<string, RenderMaterialProxy>> ObjectRenderMaterialProxiesMap { get; } = new();

  /// <summary>
  /// Returns the material proxy list for the given objects.
  /// </summary>
  /// <param name="elementIds"></param>
  /// <returns></returns>
  public List<RenderMaterialProxy> GetRenderMaterialProxyListForObjects(List<string> elementIds)
  {
    // merge all render material proxies by their material id
    // return that
    var proxiesToMerge = ObjectRenderMaterialProxiesMap
      .Where(kvp => elementIds.Contains(kvp.Key))
      .Select(kvp => kvp.Value);

    var mergeTarget = new Dictionary<string, RenderMaterialProxy>();
    foreach (var dictionary in proxiesToMerge)
    {
      foreach (var kvp in dictionary)
      {
        if (!mergeTarget.TryGetValue(kvp.Key, out RenderMaterialProxy? value))
        {
          value = kvp.Value;
          mergeTarget[kvp.Key] = value;
          continue;
        }
        value.objects.AddRange(kvp.Value.objects);
      }
    }
    foreach (var renderMaterialProxy in mergeTarget.Values)
    {
      renderMaterialProxy.objects = renderMaterialProxy.objects.Distinct().ToList();
    }
    return mergeTarget.Values.ToList();
  }
}
