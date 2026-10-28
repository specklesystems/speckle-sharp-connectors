using Speckle.Converters.Autocad.Extensions;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Autocad.ToSpeckle.Raw;

public class DBSplineToSpecklePolylineRawConverter : ITypedConverter<ADB.Spline, SOG.Polyline>
{
  private readonly ITypedConverter<List<double>, SOG.Polyline> _doublesConverter;

  private readonly IConverterSettingsStore<AutocadConversionSettings> _settingsStore;

  public DBSplineToSpecklePolylineRawConverter(
    ITypedConverter<List<double>, SOG.Polyline> doublesConverter,
    IConverterSettingsStore<AutocadConversionSettings> settingsStore
  )
  {
    _doublesConverter = doublesConverter;
    _settingsStore = settingsStore;
  }

  public Base Convert(object target) => Convert((ADB.Spline)target);

  public SOG.Polyline Convert(ADB.Spline spline)
  {
    using ADB.Curve polySpline = spline.ToPolylineWithPrecision(10, false, false);
    List<double> verticesList = new();
    switch (polySpline)
    {
      case ADB.Polyline2d o:
        verticesList = o.GetSubEntities<ADB.Vertex2d>(
            ADB.OpenMode.ForRead,
            _settingsStore.Current.Document.TransactionManager.TopTransaction
          )
          .Where(e => e.VertexType != ADB.Vertex2dType.SplineControlVertex) // POC: not validated yet!
          .SelectMany(v => o.VertexPosition(v).ToArray())
          .ToList();

        break;
      case ADB.Polyline3d o:
        verticesList = o.GetSubEntities<ADB.PolylineVertex3d>(
            ADB.OpenMode.ForRead,
            _settingsStore.Current.Document.TransactionManager.TopTransaction
          )
          .Where(e => e.VertexType != ADB.Vertex3dType.ControlVertex)
          .SelectMany(o => o.Position.ToArray())
          .ToList();
        break;
    }

    return _doublesConverter.Convert(verticesList);
  }
}
