using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Geometry;

namespace Speckle.Converters.RevitShared.Raw;

public sealed class CurveArrArrayToSpecklePolycurveConverter : ITypedConverter<DB.CurveArrArray, List<SOG.Polycurve>>
{
  private readonly ITypedConverter<DB.CurveArray, SOG.Polycurve> _curveArrayConverter;

  public CurveArrArrayToSpecklePolycurveConverter(ITypedConverter<CurveArray, Polycurve> curveArrayConverter)
  {
    _curveArrayConverter = curveArrayConverter;
  }

  public List<SOG.Polycurve> Convert(CurveArrArray target)
  {
    List<SOG.Polycurve> polycurves = new();
    foreach (var curveArray in GetCurveArrays(target))
    {
      polycurves.Add(_curveArrayConverter.Convert(curveArray));
    }

    return polycurves;
  }

  private IEnumerable<DB.CurveArray> GetCurveArrays(DB.CurveArrArray curveArrArray)
  {
    for (var i = 0; i < curveArrArray.Size; i++)
    {
      yield return curveArrArray.get_Item(i);
    }
  }
}
