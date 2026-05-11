using Microsoft.Extensions.Logging;
using Speckle.Converter.MicroStation.ToSpeckle.TopLevel;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;
using MgdElements = Bentley.DgnPlatformNET.Elements;

namespace Speckle.Converter.MicroStation.ToSpeckle;

/// <summary>
/// Root dispatcher: pattern-matches the incoming managed <see cref="MgdElement"/> against the
/// concrete <c>Bentley.DgnPlatformNET.Elements.*</c> subtypes and delegates to the corresponding
/// converter. NO COM Element ever flows through this dispatcher anymore — every leaf converter
/// takes its typed managed element directly. The pre-existing COM bridge (<c>GetElementByID64</c>)
/// is gone, along with the <c>MsdElementType</c> enum lookup table that drove it.
/// <para>
/// Per-element error handling: if a typed converter throws a managed exception, the catch falls
/// through to the bounding-box fallback so the element still ships with placeholder geometry.
/// True CSEs aren't catchable in CLR 4.x and tear down the host — we keep the leaf converters
/// to data-read-only managed APIs to minimise that risk.
/// </para>
/// </summary>
public class MicroStationRootToSpeckleConverter(
  LineElementConverter lineConverter,
  ArcElementConverter arcConverter,
  EllipseElementConverter ellipseConverter,
  LineStringElementConverter lineStringConverter,
  PointStringElementConverter pointStringConverter,
  ShapeElementConverter shapeConverter,
  ComplexShapeElementConverter complexShapeConverter,
  ComplexStringElementConverter complexStringConverter,
  BsplineCurveElementConverter bsplineCurveConverter,
  BSplineSurfaceElementConverter bsplineSurfaceConverter,
  CellHeaderElementConverter cellConverter,
  SharedCellElementConverter sharedCellConverter,
  TextElementConverter textConverter,
  SolidElementConverter solidConverter,
  MeshHeaderElementConverter meshHeaderConverter,
  FallbackElementMeshConverter fallbackConverter,
  ILogger<MicroStationRootToSpeckleConverter> logger
) : IRootToSpeckleConverter
{
  public Base Convert(object target)
  {
    if (target is not MgdElement element)
    {
      throw new InvalidOperationException(
        $"Expected a managed DGN Element (Bentley.DgnPlatformNET.Elements.Element) but got {target?.GetType().Name ?? "null"}."
      );
    }

    var applicationId = ((ulong)element.ElementId).ToString();

    try
    {
      Base result = element switch
      {
        MgdElements.MeshHeaderElement m => meshHeaderConverter.Convert(m),
        MgdElements.LineElement l => lineConverter.Convert(l),
        MgdElements.ArcElement a => arcConverter.Convert(a),
        MgdElements.EllipseElement e => ellipseConverter.Convert(e),
        MgdElements.LineStringElement ls => lineStringConverter.Convert(ls),
        MgdElements.PointStringElement ps => pointStringConverter.Convert(ps),
        MgdElements.ShapeElement sh => shapeConverter.Convert(sh),
        MgdElements.ComplexShapeElement cs => complexShapeConverter.Convert(cs),
        MgdElements.ComplexStringElement cst => complexStringConverter.Convert(cst),
        MgdElements.BSplineCurveElement bc => bsplineCurveConverter.Convert(bc),
        MgdElements.BSplineSurfaceElement bs => bsplineSurfaceConverter.Convert(bs),
        // Cell skeleton from the converter; this dispatcher fills in `elements` recursively
        // below. Keeps the leaf converter free of any back-reference to IRootToSpeckleConverter
        // (which would otherwise be a DI cycle — a cell can contain any element type).
        MgdElements.CellHeaderElement c => PopulateCellChildren(c, cellConverter.Convert(c)),
        MgdElements.SharedCellElement sc => sharedCellConverter.Convert(sc),
        MgdElements.TextElement t => textConverter.Convert(t),
        MgdElements.SurfaceOrSolidElement so => solidConverter.Convert(so),
        _ => fallbackConverter.Convert(element),
      };
      result.applicationId ??= applicationId;
      return result;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      logger.LogWarning(
        ex,
        "Top-level converter threw for {ElementType} (id {ElementId}); falling back to bounding-box mesh.",
        element.GetType().Name,
        applicationId
      );
      var fallback = fallbackConverter.Convert(element);
      fallback.applicationId ??= applicationId;
      return fallback;
    }
  }

  /// <summary>
  /// Walks the cell's child elements via the managed <c>GetChildren()</c> enumerator and
  /// recursively converts each one through this dispatcher. Children that fail to convert
  /// (managed exceptions from leaf converters) are logged and skipped — the cell skeleton
  /// is still emitted with whatever children did succeed, so the cell never disappears
  /// silently from the output.
  /// </summary>
  private Collection PopulateCellChildren(MgdElements.CellHeaderElement cell, Collection skeleton)
  {
    var children = cell.GetChildren();
    if (children == null)
    {
      return skeleton;
    }

    foreach (var child in children)
    {
      if (child == null)
      {
        continue;
      }
      try
      {
        skeleton.elements.Add(Convert(child));
      }
      catch (Exception ex) when (!ex.IsFatal())
      {
        logger.LogWarning(
          ex,
          "Skipping cell child element (id {ChildId}) — converter threw.",
          (ulong)child.ElementId
        );
      }
    }

    return skeleton;
  }
}
