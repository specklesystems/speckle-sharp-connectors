using Autodesk.Revit.DB;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.ToSpeckle;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

[NameAndRankValue(nameof(SOG.Point), 0)]
internal class PointToHostTopLevelConverter
  : BaseTopLevelConverterToHost<SOG.Point, DB.Solid>,
    ITypedConverter<SOG.Point, DB.Solid>
{
  private readonly IRevitConversionContextStack _contextStack;
  private readonly ITypedConverter<SOG.Point, DB.XYZ> _pointConverter;

  public PointToHostTopLevelConverter(
    IRevitConversionContextStack contextStack,
    ITypedConverter<SOG.Point, XYZ> pointConverter
  )
  {
    _contextStack = contextStack;
    _pointConverter = pointConverter;
  }

  public override DB.Solid Convert(SOG.Point target)
  {
    List<Curve> profile = new();

    // first create sphere with 2' radius
    XYZ center = _pointConverter.Convert(target);

    double radius = .2;
    //XYZ profile00 = center;
    XYZ profilePlus = center.Add(new XYZ(0, radius, 0));
    XYZ profileMinus = center.Subtract(new XYZ(0, radius, 0));

    profile.Add(Line.CreateBound(profilePlus, profileMinus));
    profile.Add(Arc.Create(profileMinus, profilePlus, center.Add(new XYZ(radius, 0, 0))));

    CurveLoop curveLoop = CurveLoop.Create(profile);
    SolidOptions options = new(ElementId.InvalidElementId, ElementId.InvalidElementId);

    Frame frame = new(center, XYZ.BasisX, XYZ.BasisZ.Multiply(-1), XYZ.BasisY);

    if (!Frame.CanDefineRevitGeometry(frame))
    {
      throw new SpeckleConversionException("Unable to define Revit geometry");
    }

    Solid sphere = GeometryCreationUtilities.CreateRevolvedGeometry(frame, [curveLoop], 0, 2 * Math.PI, options);

    // create direct shape and assign the sphere shape
    DirectShape ds = DirectShape.CreateElement(
      _contextStack.Current.Document,
      new ElementId(BuiltInCategory.OST_GenericModel)
    );

    ds.ApplicationId = target.applicationId ?? "appId";
    ds.ApplicationDataId = "Geometry object id";
    ds.SetShape([sphere]);

    return sphere;
  }
}
