using Microsoft.Extensions.Logging;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converter.MicroStation.ToSpeckle;

/// <summary>
/// Root dispatcher: resolves the correct <see cref="IToSpeckleTopLevelConverter"/> for a given
/// MicroStation <see cref="Element"/> and delegates the conversion. Two layers of fallback:
/// <list type="bullet">
///   <item>Element type not in <see cref="s_typeMap"/> → <see cref="FallbackElementMeshConverter"/> (bounding-box mesh)</item>
///   <item>Mapped converter throws → also fall back to bounding-box mesh, log the failure</item>
/// </list>
/// Either way the caller always gets a non-null geometric <see cref="Base"/> back, so the
/// per-element catch in the root object builder is a true error path (e.g. orphaned COM
/// reference, range read crash) rather than a converter-coverage gap.
/// <para>
/// COM RCW objects returned from the DGN element cache have a runtime type of <c>__ComObject</c>
/// or the CoClass type — neither matches the COM interface types (<c>MSIDGN.LineElement</c> etc.)
/// registered in the converter manager. We therefore dispatch via the DGN <c>MsdElementType</c>
/// enum (available on every element) and look up the registered interface type directly.
/// </para>
/// </summary>
public class MicroStationRootToSpeckleConverter(
  IConverterManager<IToSpeckleTopLevelConverter> converterManager,
  FallbackElementMeshConverter fallbackConverter,
  ILogger<MicroStationRootToSpeckleConverter> logger
) : IRootToSpeckleConverter
{
  private static readonly Dictionary<MSIDGN.MsdElementType, Type> s_typeMap =
    new()
    {
      [MSIDGN.MsdElementType.Line] = typeof(MSIDGN.LineElement),
      // LineString and PointString both surface as MSIDGN.PointStringElement in the 2026 COM API
      // (no dedicated MSIDGN.LineStringElement type).
      [MSIDGN.MsdElementType.PointString] = typeof(MSIDGN.PointStringElement),
      [MSIDGN.MsdElementType.LineString] = typeof(MSIDGN.PointStringElement),
      [MSIDGN.MsdElementType.Arc] = typeof(MSIDGN.ArcElement),
      [MSIDGN.MsdElementType.Ellipse] = typeof(MSIDGN.EllipseElement),
      [MSIDGN.MsdElementType.Text] = typeof(MSIDGN.TextElement),
      [MSIDGN.MsdElementType.CellHeader] = typeof(MSIDGN.CellElement),
      [MSIDGN.MsdElementType.SharedCell] = typeof(MSIDGN.SharedCellElement),
      [MSIDGN.MsdElementType.BsplineCurve] = typeof(MSIDGN.BsplineCurveElement),
      // Surfaces / closed regions / compound chains — common in structural & civil DGN files.
      [MSIDGN.MsdElementType.BsplineSurface] = typeof(MSIDGN.BsplineSurfaceElement),
      [MSIDGN.MsdElementType.Shape] = typeof(MSIDGN.ShapeElement),
      [MSIDGN.MsdElementType.ComplexShape] = typeof(MSIDGN.ComplexShapeElement),
      [MSIDGN.MsdElementType.ComplexString] = typeof(MSIDGN.ComplexStringElement),
      // Note: MsdElementType.MeshHeader has no typed COM wrapper in the 2026 interop; tessellated
      // mesh extraction lives in Bentley.DgnPlatformNET (CSE-prone). MeshHeader elements stay on
      // bounding-box fallback until a stable native-interop path is in place.
      // Note: MsdElementType.Solid (SmartSolidElement) is NOT mapped here on purpose. Tessellation
      // via FacetSolidAsShapes works in many cases but has historically triggered process-
      // terminating CSEs on certain solids — treated as BREP follow-up work.
    };

  public Base Convert(object target)
  {
    if (target is not Element element)
    {
      throw new InvalidOperationException($"Expected a DGN Element but got {target?.GetType().Name ?? "null"}.");
    }

    var elementId = element.ID.ToString();

    if (s_typeMap.TryGetValue(element.Type, out var interfaceType))
    {
      try
      {
        var converter = converterManager.ResolveConverter(interfaceType, recursive: false);
        var result = converter.Convert(element);
        result.applicationId ??= elementId;
        return result;
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        // Dedicated converter failed — log and fall through to bounding-box mesh so the element
        // still ships with at least placeholder geometry rather than getting dropped entirely.
        logger.LogWarning(
          ex,
          "Top-level converter for {ElementType} (id {ElementId}) threw; falling back to bounding-box mesh.",
          element.Type,
          elementId
        );
      }
    }

    var fallback = fallbackConverter.Convert(element);
    fallback.applicationId ??= elementId;
    return fallback;
  }
}
