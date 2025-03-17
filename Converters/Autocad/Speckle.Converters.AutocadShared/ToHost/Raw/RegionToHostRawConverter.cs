using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class RegionToHostRawConverter : ITypedConverter<SOG.Region, ADB.Region>
{
  private readonly ITypedConverter<ICurve, ADB.Curve> _curveConverter;
  private readonly ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> _polycurveConverter;

  public RegionToHostRawConverter(
    ITypedConverter<ICurve, ADB.Curve> curveConverter,
    ITypedConverter<SOG.Polycurve, List<(ADB.Entity, Base)>> polycurveConverter
  )
  {
    _curveConverter = curveConverter;
    _polycurveConverter = polycurveConverter;
  }

  public ADB.Region Convert(SOG.Region target)
  {
    // The curveSegments must contain only Line, Arc, Ellipse, Circle, Spline, Polyline3d, or Polyline2d objects.
    // The objects in curveSegments must be opened for read and not for write. If the objects are opened, calling this function will crash AutoCAD.

    // Add coverted boundary and loops segments to the segmentCollection
    var segmentCollection = new List<ADB.Curve>();
    segmentCollection.AddRange(ConvertICurveToSegments(target.boundary));

    foreach (var loop in target.innerLoops)
    {
      segmentCollection.AddRange(ConvertICurveToSegments(loop));
    }

    // add all segments to the ADB.DBObjectCollection
    ADB.DBObjectCollection acDBObjColl = new();
    foreach (var segment in segmentCollection)
    {
      acDBObjColl.Add(segment);
    }

    // Calculate the regions based on each closed loop
    // https://help.autodesk.com/view/OARX/2025/ENU/?guid=GUID-9CD22AE5-8F66-4925-A155-95852BAFD565
    using (ADB.DBObjectCollection myRegionColl = ADB.Region.CreateFromCurves(acDBObjColl))
    {
      if (myRegionColl[0] is ADB.Region adbRegion)
      {
        return adbRegion;
      }
    }

    throw new ConversionException($"Region conversion failed: {target}");
  }

  private List<ADB.Curve> ConvertICurveToSegments(ICurve curve)
  {
    var segments = new List<ADB.Curve>();
    if (curve is SOG.Polycurve polycurve)
    {
      segments.AddRange(_polycurveConverter.Convert(polycurve).Select(x => x.Item1).Cast<ADB.Curve>());
    }
    else
    {
      segments.Add(_curveConverter.Convert(curve));
    }

    return segments;
  }
}
