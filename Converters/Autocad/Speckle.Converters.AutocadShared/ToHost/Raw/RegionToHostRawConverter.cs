using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class RegionToHostRawConverter : ITypedConverter<SOG.Region, ADB.Region>
{
  private readonly ITypedConverter<ICurve, ADB.Curve> _curveConverter;

  public RegionToHostRawConverter(ITypedConverter<ICurve, ADB.Curve> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public ADB.Region Convert(SOG.Region target)
  {
    // Notes: The curveSegments must contain only Line, Arc, Ellipse, Circle, Spline, Polyline3d, or Polyline2d objects.
    // The objects in curveSegments must be opened for read and not for write. If the objects are opened, calling this function will crash AutoCAD.

    // Add converted boundary to the segmentCollection
    // TODO: check if boundary is a polycurve
    ADB.Curve boundarySegmentCollection = _curveConverter.Convert(target.boundary);

    // Add converted loops to the list if segmentCollections
    var loopsSegmentCollection = new List<ADB.Curve>();
    foreach (var loop in target.innerLoops)
    {
      loopsSegmentCollection.Add(_curveConverter.Convert(loop));
    }

    // add all boundary segments to the ADB.DBObjectCollection
    ADB.DBObjectCollection boundaryDBObjColl = new();
    boundaryDBObjColl.Add(boundarySegmentCollection);

    // Calculate the outer region, method should return an array with 1 region
    // https://help.autodesk.com/view/OARX/2025/ENU/?guid=GUID-684E602E-3555-4370-BCDC-1CE594676C43
    using (ADB.DBObjectCollection outerRegionColl = ADB.Region.CreateFromCurves(boundaryDBObjColl))
    {
      if (outerRegionColl.Count != 1)
      {
        throw new ConversionException(
          $"Region conversion failed for {target}: unexpected number of shapes generated ({outerRegionColl.Count}). Make sure that input loops are planar, closed, non self-intersecting curves."
        );
      }
      if (outerRegionColl[0] is ADB.Region adbRegion)
      {
        // Create and subtract the inner loops' regions, iterate through each
        foreach (var loopSegmentCollection in loopsSegmentCollection)
        {
          // add loop segments to the ADB.DBObjectCollection
          ADB.DBObjectCollection loopDBObjColl = new();
          loopDBObjColl.Add(loopSegmentCollection);

          // Same as above: calculate the inner region, method should return an array with 1 region
          using (ADB.DBObjectCollection innerRegionColl = ADB.Region.CreateFromCurves(loopDBObjColl))
          {
            if (innerRegionColl.Count != 1)
            {
              throw new ConversionException(
                $"Region conversion failed for {target}: unexpected number of shapes generated ({innerRegionColl.Count}). Make sure that input loops are planar, closed, non self-intersecting curves."
              );
            }
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
}
