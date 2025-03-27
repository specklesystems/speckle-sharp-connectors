using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToHost.Raw;

public class RegionToHostRawConverter : ITypedConverter<SOG.Region, ADB.Region>
{
  private readonly ITypedConverter<ICurve, List<(ADB.Entity, Base)>> _curveConverter;

  public RegionToHostRawConverter(ITypedConverter<ICurve, List<(ADB.Entity, Base)>> curveConverter)
  {
    _curveConverter = curveConverter;
  }

  public ADB.Region Convert(SOG.Region target)
  {
    // Notes from docs: The curveSegments must contain only Line, Arc, Ellipse, Circle, Spline, Polyline3d, or Polyline2d objects.
    // The objects in curveSegments must be opened for read and not for write. If the objects are opened, calling this function will crash AutoCAD.

    // Converted boundary
    List<(ADB.Entity, Base)> convertedBoundary = _curveConverter.Convert(target.boundary);
    ADB.Curve nativeBoundary = ValidateCurve(convertedBoundary);

    // Converted loops
    var nativeLoops = new List<ADB.Curve>();
    foreach (var loop in target.innerLoops)
    {
      List<(ADB.Entity, Base)> convertedLoop = _curveConverter.Convert(loop);
      nativeLoops.Add(ValidateCurve(convertedLoop));
    }

    // Add boundary to the ADB.DBObjectCollection
    // Calculate the outer region, method should return an array with 1 region
    // https://help.autodesk.com/view/OARX/2025/ENU/?guid=GUID-684E602E-3555-4370-BCDC-1CE594676C43
    ADB.DBObjectCollection boundaryDBObjColl = new();
    boundaryDBObjColl.Add(nativeBoundary);
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
        foreach (var nativeLoop in nativeLoops)
        {
          // Same as above: Add loop segments to the ADB.DBObjectCollection
          // Calculate the inner region, method should return an array with 1 region
          ADB.DBObjectCollection loopDBObjColl = new();
          loopDBObjColl.Add(nativeLoop);
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

  private ADB.Curve ValidateCurve(List<(ADB.Entity, Base)> convertedResult)
  {
    if (convertedResult.Count != 1)
    {
      // this will only be the case if it was a non-planar Polycurve: throw error
      throw new ConversionException($"Non-planar Polycurve cannot be used as a Region loop: {convertedResult}");
    }
    return (ADB.Curve)convertedResult[0].Item1;
  }
}
