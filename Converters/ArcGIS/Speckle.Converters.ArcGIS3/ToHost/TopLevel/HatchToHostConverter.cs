using Speckle.Converters.ArcGIS3.Utils;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Objects.Other;
using Speckle.Sdk.Models;

namespace Speckle.Converters.ArcGIS3.ToHost.TopLevel;

[NameAndRankValue(nameof(Hatch), NameAndRankValueAttribute.SPECKLE_DEFAULT_RANK)]
public class HatchToHostConverter : IToHostTopLevelConverter, ITypedConverter<Hatch, ACG.Polygon>
{
  private readonly IRootToHostConverter _converter;

  public HatchToHostConverter(IRootToHostConverter converter)
  {
    _converter = converter;
  }

  public object Convert(Base target) => Convert((Hatch)target);

  public ACG.Polygon Convert(Hatch target)
  {
    HatchLoop? boundarySpeckle = target.loops.FirstOrDefault(x => x.Type == HatchLoopType.Outer);
    if (boundarySpeckle is null && target.loops.Count == 1)
    {
      boundarySpeckle = target.loops[0];
    }

    if (boundarySpeckle is null)
    {
      throw new SpeckleConversionException("Invalid Hatch provided");
    }

    List<HatchLoop> voidsSpeckle = target.loops.Where(x => x.Type == HatchLoopType.Inner).ToList();

    ACG.Polyline? boundary = (ACG.Polyline)_converter.Convert((Base)boundarySpeckle.Curve);

    // enforce clockwise outer ring orientation: https://pro.arcgis.com/en/pro-app/latest/sdk/api-reference/topic72904.html
    if (!boundary.IsClockwisePolygon())
    {
      boundary = ACG.GeometryEngine.Instance.ReverseOrientation(boundary) as ACG.Polyline;
    }

    if (boundary is null)
    {
      throw new SpeckleConversionException("Hatch conversion of boundary curve failed");
    }

    ACG.PolygonBuilderEx polyOuterRing = new(boundary.Parts.SelectMany(x => x), ACG.AttributeFlags.HasZ);

    // adding inner loops: https://github.com/esri/arcgis-pro-sdk/wiki/ProSnippets-Geometry#build-a-donut-polygon
    foreach (HatchLoop loop in voidsSpeckle)
    {
      ACG.Polyline? loopNative = (ACG.Polyline)_converter.Convert((Base)loop.Curve);

      // enforce clockwise outer ring orientation
      if (loopNative.IsClockwisePolygon())
      {
        loopNative = ACG.GeometryEngine.Instance.ReverseOrientation(loopNative) as ACG.Polyline;
      }

      if (loopNative is null)
      {
        throw new SpeckleConversionException("Hatch conversion of inner loop failed");
      }

      polyOuterRing.AddPart(loopNative.Parts.SelectMany(x => x.ToList()));
    }

    var resultPolygon = polyOuterRing.ToGeometry();
    return resultPolygon;
  }
}
