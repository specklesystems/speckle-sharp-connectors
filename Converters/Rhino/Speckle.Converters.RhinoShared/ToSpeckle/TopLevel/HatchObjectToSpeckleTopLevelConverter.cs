using Rhino.DocObjects;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Rhino.ToSpeckle.TopLevel;

[NameAndRankValue(typeof(HatchObject), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchObjectToSpeckleTopLevelConverter : IToSpeckleTopLevelConverter
{
  private readonly ITypedConverter<RG.NurbsCurve, SOG.Curve> _nurbsConverter;
  private readonly IConverterSettingsStore<RhinoConversionSettings> _settingsStore;

  public HatchObjectToSpeckleTopLevelConverter(
    IConverterSettingsStore<RhinoConversionSettings> settingsStore,
    ITypedConverter<RG.NurbsCurve, SOG.Curve> nurbsConverter
  )
  {
    _settingsStore = settingsStore;
    _nurbsConverter = nurbsConverter;
  }

  public Base Convert(object target)
  {
    var hatchObject = (HatchObject)target;

    // get boundary and inner curves
    RG.Curve rhinoBoundary = ((RG.Hatch)hatchObject.Geometry).Get3dCurves(true)[0];
    RG.Curve[] rhinoLoops = ((RG.Hatch)hatchObject.Geometry).Get3dCurves(false);

    SOG.Curve boundary = _nurbsConverter.Convert((RG.NurbsCurve)rhinoBoundary);
    List<SOG.Curve> innerLoops = rhinoLoops.Select(x => _nurbsConverter.Convert((RG.NurbsCurve)x)).ToList();

    List<ICurve> allCurves = new() { boundary };
    allCurves.AddRange(innerLoops);

    var region = new SOG.Region
    {
      boundary = boundary,
      innerLoops = innerLoops.Cast<ICurve>().ToList(),
      hasHatchPattern = true,
      bbox = null,
      displayValue = allCurves,
      units = _settingsStore.Current.SpeckleUnits,
    };

    return region;
  }
}
