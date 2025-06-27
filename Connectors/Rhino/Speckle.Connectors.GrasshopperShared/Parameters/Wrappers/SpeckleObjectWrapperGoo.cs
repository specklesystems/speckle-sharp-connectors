using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData
{
  public override bool IsValid => Value.Base is not null && Value.ApplicationId is not null;
  public override string TypeName => "Speckle Object";
  public override string TypeDescription => "Represents a geometry object from Speckle";

  public SpeckleObjectWrapperGoo(SpeckleObjectWrapper value)
  {
    Value = value;
  }

  /// <summary>Parameterless constructor</summary>
  /// <remarks>Should only be used for casting!</remarks>
  public SpeckleObjectWrapperGoo()
  {
    Value = new()
    {
      Base = new(),
      GeometryBase = null,
      Color = null,
      Material = null,
    };
  }

  public override IGH_Goo Duplicate() => new SpeckleObjectWrapperGoo(Value.DeepCopy());

  public override string ToString() =>
    $"Speckle Object : {(string.IsNullOrWhiteSpace(Value.Name) ? Value.Base.speckle_type : Value.Name)}]";

  /// <summary>
  /// Casts from Speckle objects, geometry base, and model objects.
  /// All non-Speckle objects will be converted to its geometry equivalent.
  /// </summary>
  /// <param name="source"></param>
  /// <returns></returns>
  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleObjectWrapper wrapper:
        Value = wrapper.DeepCopy();
        return true;
      case SpeckleObjectWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value.DeepCopy();
        return true;
      case SpeckleBlockInstanceWrapperGoo instanceWrapperGoo:
        Value = instanceWrapperGoo.Value;
        return true;
      case IGH_GeometricGoo geometricGoo:
        GeometryBase gb = geometricGoo.ToGeometryBase();
        Base converted = SpeckleConversionContext.ConvertToSpeckle(gb);
        string appId = Guid.NewGuid().ToString();
        Value = gb is InstanceReferenceGeometry instance
          ? new SpeckleBlockInstanceWrapper()
          {
            GeometryBase = gb,
            Base = converted,
            Transform = instance.Xform,
            ApplicationId = appId,
          }
          : new SpeckleObjectWrapper()
          {
            GeometryBase = gb,
            Base = converted,
            ApplicationId = appId
          };
        return true;
    }

    return CastFromModelObject(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  public override bool CastTo<T>(ref T target)
  {
    if (Value.GeometryBase == null)
    {
      return CastToModelObject(ref target);
    }

    return target switch
    {
      GH_Surface => TryCastToSurface(ref target),
      GH_Mesh => TryCastToMesh(ref target),
      GH_Brep => TryCastToBrep(ref target),
      GH_Line => TryCastToLine(ref target),
      GH_Curve => TryCastToCurve(ref target),
      GH_Point => TryCastToPoint(ref target),
      GH_Circle => TryCastToCircle(ref target),
      GH_Arc => TryCastToArc(ref target),
#if RHINO8_OR_GREATER
      GH_Extrusion => TryCastToExtrusion(ref target),
      GH_PointCloud => TryCastToPointcloud(ref target),
      GH_SubD => TryCastToSubD(ref target),
      GH_Hatch => TryCastToHatch(ref target),
#endif
      IGH_GeometricGoo => TryCastToGeometricGoo(ref target),
      _ => CastToModelObject(ref target)
    };
  }

  private bool TryCastToSurface<T>(ref T target)
  {
    Surface? surface = null;
    if (GH_Convert.ToSurface(Value.GeometryBase, ref surface, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Surface(surface);
      return true;
    }
    return false;
  }

  private bool TryCastToMesh<T>(ref T target)
  {
    Mesh? mesh = null;
    if (GH_Convert.ToMesh(Value.GeometryBase, ref mesh, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Mesh(mesh);
      return true;
    }
    return false;
  }

  private bool TryCastToBrep<T>(ref T target)
  {
    Brep? brep = null;
    if (GH_Convert.ToBrep(Value.GeometryBase, ref brep, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Brep(brep);
      return true;
    }
    return false;
  }

  private bool TryCastToLine<T>(ref T target)
  {
    Line line = new();
    if (GH_Convert.ToLine(Value.GeometryBase, ref line, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Line(line);
      return true;
    }
    return false;
  }

  private bool TryCastToCurve<T>(ref T target)
  {
    Curve? curve = null;
    if (GH_Convert.ToCurve(Value.GeometryBase, ref curve, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Curve(curve);
      return true;
    }
    return false;
  }

  private bool TryCastToPoint<T>(ref T target)
  {
    Point3d point = new();
    if (GH_Convert.ToPoint3d(Value.GeometryBase, ref point, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Point(point);
      return true;
    }
    return false;
  }

  private bool TryCastToGeometricGoo<T>(ref T target)
  {
    var geometricGoo = GH_Convert.ToGeometricGoo(Value.GeometryBase);
    if (geometricGoo != null && geometricGoo is T convertedGoo)
    {
      target = convertedGoo;
      return true;
    }
    return false;
  }

  private bool TryCastToCircle<T>(ref T target)
  {
    var circle = new Circle();
    if (GH_Convert.ToCircle(Value.GeometryBase, ref circle, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Circle(circle);
      return true;
    }
    return false;
  }

  private bool TryCastToArc<T>(ref T target)
  {
    var arc = new Arc();
    if (GH_Convert.ToArc(Value.GeometryBase, ref arc, GH_Conversion.Both))
    {
      target = (T)(object)new GH_Arc(arc);
      return true;
    }
    return false;
  }

  public void DrawViewportWires(GH_PreviewWireArgs args)
  {
    // TODO ?
  }

  public void DrawViewportMeshes(GH_PreviewMeshArgs args)
  {
    Value.DrawPreviewRaw(args.Pipeline, args.Material);
  }

  BoundingBox IGH_PreviewData.ClippingBox =>
    Value.GeometryBase is null ? new() : Value.GeometryBase.GetBoundingBox(false);
}
