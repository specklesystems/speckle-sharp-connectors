using Speckle.Converters.Common;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle;

/// <summary>
/// Root dispatcher: resolves the correct <see cref="IToSpeckleTopLevelConverter"/> for a given
/// MicroStation <see cref="Element"/> and delegates the conversion. Unmapped types fall through
/// to <see cref="FallbackElementMeshConverter"/> (currently a bounding-box mesh) so nothing is
/// silently dropped.
/// <para>
/// COM RCW objects returned from the DGN element cache have a runtime type of <c>__ComObject</c>
/// or the CoClass type — neither matches the COM interface types (<c>MSIDGN.LineElement</c> etc.)
/// registered in the converter manager. We therefore dispatch via the DGN <c>MsdElementType</c>
/// enum (available on every element) and look up the registered interface type directly.
/// </para>
/// </summary>
public class MicroStationRootToSpeckleConverter(
  IConverterManager<IToSpeckleTopLevelConverter> converterManager,
  FallbackElementMeshConverter fallbackConverter
) : IRootToSpeckleConverter
{
  private static readonly Dictionary<MSIDGN.MsdElementType, Type> s_typeMap =
    new()
    {
      [MSIDGN.MsdElementType.Line] = typeof(MSIDGN.LineElement),
      // LineString and PointString both use PointStringElement in the 2026 COM API
      [MSIDGN.MsdElementType.PointString] = typeof(MSIDGN.PointStringElement),
      [MSIDGN.MsdElementType.Arc] = typeof(MSIDGN.ArcElement),
      [MSIDGN.MsdElementType.Ellipse] = typeof(MSIDGN.EllipseElement),
      [MSIDGN.MsdElementType.Text] = typeof(MSIDGN.TextElement),
      [MSIDGN.MsdElementType.CellHeader] = typeof(MSIDGN.CellElement),
      [MSIDGN.MsdElementType.SharedCell] = typeof(MSIDGN.SharedCellElement),
      [MSIDGN.MsdElementType.BsplineCurve] = typeof(MSIDGN.BsplineCurveElement),
    };

  public Base Convert(object target)
  {
    if (target is not Element element)
    {
      throw new InvalidOperationException($"Expected a DGN Element but got {target?.GetType().Name ?? "null"}.");
    }

    if (!s_typeMap.TryGetValue(element.Type, out var interfaceType))
    {
      var fallback = fallbackConverter.Convert(element);
      fallback.applicationId ??= element.ID.ToString();
      return fallback;
    }

    var converter = converterManager.ResolveConverter(interfaceType, recursive: false);
    var result = converter.Convert(element);
    result.applicationId ??= element.ID.ToString();
    return result;
  }
}
