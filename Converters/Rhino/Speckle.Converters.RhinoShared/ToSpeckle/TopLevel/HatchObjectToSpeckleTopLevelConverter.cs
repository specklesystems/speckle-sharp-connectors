using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(HatchObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.Curve, ICurve> _curveConverter;
  private readonly ITypedConverter<RG.Point3d, SOG.Point> _pointConverter;
  private readonly ITypedConverter<RG.Vector3d, SOG.Vector> _vectorConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public HatchObjectToSpeckleTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<RG.Point3d, SOG.Point> pointConverter,
    ITypedConverter<RG.Vector3d, SOG.Vector> vectorConverter,
    ITypedConverter<RG.Curve, ICurve> curveConverter
  )
  {
    _settingsStore = settingsStore;
    _pointConverter = pointConverter;
    _vectorConverter = vectorConverter;
    _curveConverter = curveConverter;
  }

  public Base Convert(object target)
  {
    var hatchObject = (HatchObject)target;
    var hatch = (RG.Hatch)hatchObject.Geometry;

    // get boundary and inner curves
    RG.Curve rhinoBoundary = (hatch).Get3dCurves(true)[0];
    RG.Curve[] rhinoLoops = (hatch).Get3dCurves(false);

    ICurve boundary = _curveConverter.Convert(rhinoBoundary);
    List<ICurve> innerLoops = rhinoLoops.Select(x => _curveConverter.Convert(x)).ToList();

    List<ICurve> allCurves = new List<ICurve> { boundary }
      .Concat(innerLoops)
      .ToList();

    SOG.Box bbox = GetBboxFromHatch(hatch);

    return new SOG.Region
    {
      boundary = boundary,
      innerLoops = innerLoops,
      hasHatchPattern = true,
      bbox = bbox,
      displayValue = allCurves.Cast<Base>().ToList(),
      units = _settingsStore.Current.SpeckleUnits,
    };
  }

  private SOG.Box GetBboxFromHatch(RG.Hatch hatch)
  {
    // input accurate=false performs much faster but creates an approximated bbox
    var rhinoBbox = hatch.GetBoundingBox(false);
    var hatchPlane = new SOG.Plane()
    {
      origin = _pointConverter.Convert(hatch.Plane.Origin),
      normal = _vectorConverter.Convert(hatch.Plane.Normal),
      xdir = _vectorConverter.Convert(hatch.Plane.XAxis),
      ydir = _vectorConverter.Convert(hatch.Plane.YAxis),
      units = _settingsStore.Current.SpeckleUnits,
    };

    return new()
    {
      plane = hatchPlane,
      xSize = new SOP.Interval() { start = rhinoBbox.Min.X, end = rhinoBbox.Max.X },
      ySize = new SOP.Interval() { start = rhinoBbox.Min.Y, end = rhinoBbox.Max.Y },
      zSize = new SOP.Interval() { start = rhinoBbox.Min.Z, end = rhinoBbox.Max.Z },
      units = _settingsStore.Current.SpeckleUnits,
    };
  }
}
