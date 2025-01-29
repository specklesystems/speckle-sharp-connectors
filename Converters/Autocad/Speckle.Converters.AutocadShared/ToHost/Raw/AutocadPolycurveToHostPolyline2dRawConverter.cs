using Speckle.Converters.Autocad;
using Speckle.Converters.Autocad.Extensions;
using Speckle.Converters.Common;
using Speckle.Sdk.Common;
using static Speckle.Converters.Common.Result;

namespace Speckle.Converters.Autocad2023.ToHost.Raw;

public class AutocadPolycurveToHostPolyline2dRawConverter(
  ITypedConverter<SOG.Vector, AG.Vector3d> vectorConverter,
  IConverterSettingsStore<AutocadConversionSettings> settingsStore
) : ITypedConverter<SOG.Autocad.AutocadPolycurve, ADB.Polyline2d>
{
  public Result<ADB.Polyline2d> Convert(SOG.Autocad.AutocadPolycurve target)
  {
    // check for normal
    if (target.normal is not SOG.Vector normal)
    {
      return Error<ADB.Polyline2d>($"Autocad polycurve of type {target.polyType} did not have a normal");
    }

    // check for elevation
    if (target.elevation is not double elevation)
    {
      return Error<ADB.Polyline2d>($"Autocad polycurve of type {target.polyType} did not have an elevation");
    }

    // get vertices
    double f = Units.GetConversionFactor(target.units, settingsStore.Current.SpeckleUnits);
    List<AG.Point3d> points = target.value.ConvertToPoint3d(f);

    // check for invalid bulges
    if (target.bulges is null || target.bulges.Count < points.Count)
    {
      return Error<ADB.Polyline2d>($"Autocad polycurve of type {target.polyType} had null or malformed bulges");
    }

    // check for invalid tangents
    if (target.tangents is null || target.tangents.Count < points.Count)
    {
      return Error<ADB.Polyline2d>($"Autocad polycurve of type {target.polyType} had null or malformed tangents");
    }

    // create the polyline2d using the empty constructor
    if (!vectorConverter.Try(normal, out Result<AG.Vector3d> convertedNormal))
    {
      return convertedNormal.Failure<ADB.Polyline2d>();
    }
    double convertedElevation = elevation * f;
    ADB.Polyline2d polyline =
      new()
      {
        Elevation = convertedElevation,
        Normal = convertedNormal.Value,
        Closed = target.closed
      };

    // add polyline2d to document
    ADB.Transaction tr = settingsStore.Current.Document.TransactionManager.TopTransaction;
    var btr = (ADB.BlockTableRecord)
      tr.GetObject(settingsStore.Current.Document.Database.CurrentSpaceId, ADB.OpenMode.ForWrite);
    btr.AppendEntity(polyline);
    tr.AddNewlyCreatedDBObject(polyline, true);

    // append vertices
    for (int i = 0; i < points.Count; i++)
    {
      double? tangent = target.tangents[i];
      ADB.Vertex2d vertex = new(points[i], target.bulges[i], 0, 0, tangent ?? 0);
      if (tangent is not null)
      {
        vertex.TangentUsed = true;
      }

      polyline.AppendVertex(vertex);
      tr.AddNewlyCreatedDBObject(vertex, true);
    }

    // convert to polytype
    ADB.Poly2dType polyType = ADB.Poly2dType.SimplePoly;
    switch (target.polyType)
    {
      case SOG.Autocad.AutocadPolyType.FitCurve2d:
        polyType = ADB.Poly2dType.FitCurvePoly;
        break;
      case SOG.Autocad.AutocadPolyType.CubicSpline2d:
        polyType = ADB.Poly2dType.CubicSplinePoly;
        break;
      case SOG.Autocad.AutocadPolyType.QuadSpline2d:
        polyType = ADB.Poly2dType.QuadSplinePoly;
        break;
    }

    if (polyType is not ADB.Poly2dType.SimplePoly)
    {
      polyline.ConvertToPolyType(polyType);
    }

    return Success(polyline);
  }
}
