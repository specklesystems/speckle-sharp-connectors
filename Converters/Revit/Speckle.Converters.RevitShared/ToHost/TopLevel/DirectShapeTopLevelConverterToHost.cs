using Speckle.Objects;
using Speckle.Objects.Geometry;
using Speckle.Converters.Common;
using Speckle.Converters.Common.Objects;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.ToSpeckle;

namespace Speckle.Converters.RevitShared.ToHost.TopLevel;

[NameAndRankValue(nameof(SOBR.DirectShape), 0)]
public sealed class DirectShapeTopLevelConverterToHost(
  IRevitConversionContextStack contextStack,
  ITypedConverter<Brep, DB.Solid> brepConverter,
  ITypedConverter<ICurve, DB.CurveArray> curveConverter,
  ITypedConverter<SOG.Mesh, DB.GeometryObject[]> meshConverter,
  IRevitCategories revitCategories,
  ParameterValueSetter parameterValueSetter
) : BaseTopLevelConverterToHost<SOBR.DirectShape, List<DB.GeometryObject>>
{
  public override List<DB.GeometryObject> Convert(SOBR.DirectShape target)
  {
    var converted = new List<DB.GeometryObject>();

    target.baseGeometries.ForEach(b =>
    {
      switch (b)
      {
        case SOG.Brep brep:
          converted.Add(brepConverter.Convert(brep));
          break;
        case SOG.Mesh mesh:
          converted.AddRange(meshConverter.Convert(mesh));
          break;
        case ICurve curve:
          converted.AddRange(curveConverter.Convert(curve).Cast<DB.Curve>());
          break;
        default:
          throw new SpeckleConversionException(
            $"Incompatible geometry type: {b.GetType()} is not supported in DirectShape conversions."
          );
      }
    });

    //from 2.16 onwards use the builtInCategory field for direct shape fallback
    DB.BuiltInCategory bic = DB.BuiltInCategory.OST_GenericModel;
    if (!Enum.TryParse(target["builtInCategory"] as string, out bic))
    {
      //pre 2.16 or coming from grasshopper, using the enum
      //TODO: move away from enum logic
      if ((int)target.category != -1)
      {
        var bicName = revitCategories.GetBuiltInFromSchemaBuilderCategory(target.category);
#pragma warning disable IDE0002 // Simplify Member Access
        _ = DB.BuiltInCategory.TryParse(bicName, out bic);
#pragma warning restore IDE0002 // Simplify Member Access
      }
    }

    var cat = contextStack.Current.Document.Settings.Categories.get_Item(bic);

    using var revitDs = DB.DirectShape.CreateElement(contextStack.Current.Document, cat.Id);
    if (target.applicationId != null)
    {
      revitDs.ApplicationId = target.applicationId;
    }

    revitDs.ApplicationDataId = Guid.NewGuid().ToString();
    revitDs.SetShape(converted);
    revitDs.Name = target.name;
    parameterValueSetter.SetInstanceParameters(revitDs, target);

    return converted;
  }
}
