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
    // Notes: The curveSegments must contain only Line, Arc, Ellipse, Circle, Spline, Polyline3d, or Polyline2d objects.
    // The objects in curveSegments must be opened for read and not for write. If the objects are opened, calling this function will crash AutoCAD.

    // Add converted boundary to the segmentCollection
    var boundarySegmentCollection = new List<ADB.Curve>();
    boundarySegmentCollection.AddRange(ConvertICurveToSegments(target.boundary));

    // Add converted loops to the list if segmentCollections
    var loopsSegmentCollection = new List<List<ADB.Curve>>();
    foreach (var loop in target.innerLoops)
    {
      loopsSegmentCollection.Add(ConvertICurveToSegments(loop));
    }

    // add all boundary segments to the ADB.DBObjectCollection
    ADB.DBObjectCollection boundaryDBObjColl = new();
    boundarySegmentCollection.ForEach(x => boundaryDBObjColl.Add(x));

    // Calculate the outer region, method should return an array with 1 region
    // https://help.autodesk.com/view/OARX/2025/ENU/?guid=GUID-684E602E-3555-4370-BCDC-1CE594676C43
    using (ADB.DBObjectCollection outerRegionColl = ADB.Region.CreateFromCurves(boundaryDBObjColl))
    {
      if (outerRegionColl[0] is ADB.Region adbRegion)
      {
        // Create and subtract the inner loops' regions, iterate through each
        foreach (var loopSegmentCollection in loopsSegmentCollection)
        {
          // add loop segments to the ADB.DBObjectCollection
          ADB.DBObjectCollection loopDBObjColl = new();
          loopSegmentCollection.ForEach(x => loopDBObjColl.Add(x));

          // Same as above: calculate the inner region, method should return an array with 1 region
          using (ADB.DBObjectCollection innerRegionColl = ADB.Region.CreateFromCurves(loopDBObjColl))
          {
            if (innerRegionColl[0] is ADB.Region adbInnerRegion)
            {
              // substract region from Boundary region
              adbRegion.BooleanOperation(ADB.BooleanOperationType.BoolSubtract, adbInnerRegion);
              adbInnerRegion.Dispose();
            }
          }
        }

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
