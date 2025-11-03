using Speckle.Converters.Autocad;
using Speckle.Converters.Autocad.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Common;

namespace Speckle.Converters.Autocad2023.ToHost.Raw;

public class AutocadPolycurveToHostPolyline3dRawConverter
  : ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline3d>
{
  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public AutocadPolycurveToHostPolyline3dRawConverter(IConverterSettingsStore<AutocadConversionSettings> settingsStore)
  {
    _settingsStore = settingsStore;
  }

  public ADB.Polyline3d Convert(SOG.Autocad.AutocadPolycurve target)
  {
    // get vertices
    double f = Units.GetConversionFactor(target.units, _settingsStore.Current.SpeckleUnits);
    List<AG.Point3d> points = target.value.ConvertToPoint3dFromWcsToOcs(f);

    // create the polyline3d using the empty constructor
    ADB.Polyline3d polyline = new() { Closed = target.closed };

    // add polyline3d to document
    ADB.Transaction tr = _settingsStore.Current.Document.TransactionManager.TopTransaction;
    var btr = (ADB.BlockTableRecord)
      tr.GetObject(_settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);
    btr.AppendEntity(polyline);
    tr.AddNewlyCreatedDBObject(polyline, true);

    // append vertices
    for (int i = 0; i < points.Count; i++)
    {
      ADB.PolylineVertex3d vertex = new(points[i]);
      polyline.AppendVertex(vertex);
      tr.AddNewlyCreatedDBObject(vertex, true);
    }

    // convert to polytype
    ADB.Poly3dType polyType = ADB.Poly3dType.SimplePoly;
    switch (target.polyType)
    {
      case SOG.Autocad.AutocadPolyType.CubicSpline3d:
        polyType = ADB.Poly3dType.CubicSplinePoly;
        break;
      case SOG.Autocad.AutocadPolyType.QuadSpline3d:
        polyType = ADB.Poly3dType.QuadSplinePoly;
        break;
    }

    if (polyType is not ADB.Poly3dType.SimplePoly)
    {
      polyline.ConvertToPolyType(polyType);
    }

    return polyline;
  }
}
