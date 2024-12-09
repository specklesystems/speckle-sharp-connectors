using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Parameters;

/// <summary>
/// Wrapper around a received speckle object. It encapsulates the original object, its converted form and its original path.
/// </summary>
public class SpeckleObject : Base
{
  public Base OriginalObject { get; set; }
  public GeometryBase GeometryBase { get; set; }
  public List<Collection> Path { get; set; }

  // RenderMaterial, ColorProxies, Properties (?)
  public override string ToString() => $"Speckle Wrapper [{GeometryBase.GetType().Name}]";

  public void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
  {
    switch (GeometryBase)
    {
      case Mesh m:
        args.Display.DrawMeshShaded(m, isSelected ? args.ShadeMaterial_Selected : args.ShadeMaterial);
        break;
      case Brep b:
        args.Display.DrawBrepShaded(b, isSelected ? args.ShadeMaterial_Selected : args.ShadeMaterial);
        args.Display.DrawBrepWires(
          b,
          isSelected ? args.WireColour_Selected : args.WireColour,
          args.DefaultCurveThickness
        );
        break;
      case Extrusion e:
        args.Display.DrawMeshShaded(
          e.GetMesh(MeshType.Any),
          isSelected ? args.ShadeMaterial_Selected : args.ShadeMaterial
        );
        break;
      case SubD d:
        args.Display.DrawSubDShaded(d, isSelected ? args.ShadeMaterial_Selected : args.ShadeMaterial);
        args.Display.DrawSubDWires(
          d,
          isSelected ? args.WireColour_Selected : args.WireColour,
          args.DefaultCurveThickness
        );
        break;
      case Curve c:
        args.Display.DrawCurve(c, isSelected ? args.WireColour_Selected : args.WireColour, args.DefaultCurveThickness);
        break;
    }
  }

  public void DrawPreviewRaw(DisplayPipeline display, DisplayMaterial material)
  {
    switch (GeometryBase)
    {
      case Mesh m:
        display.DrawMeshShaded(m, material);
        break;
      case Brep b:
        display.DrawBrepShaded(b, material);
        display.DrawBrepWires(b, material.Diffuse);
        break;
      case Extrusion e:
        display.DrawMeshShaded(e.GetMesh(MeshType.Any), material);
        break;
      case SubD d:
        display.DrawSubDShaded(d, material);
        display.DrawSubDWires(d, material.Diffuse, display.DefaultCurveThickness);
        break;
      case Curve c:
        display.DrawCurve(c, material.Diffuse);
        break;
    }
  }
}

public class SpeckleObjectGoo : GH_Goo<SpeckleObject>, IGH_PreviewData
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Object Goo [{m_value.OriginalObject.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle object wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper objects.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleObject speckleGrasshopperObject:
        Value = speckleGrasshopperObject;
        return true;
      case GH_Goo<SpeckleObject> speckleGrasshopperObjectGoo:
        Value = speckleGrasshopperObjectGoo.Value;
        return true;
    }

    return false;
  }

  public override bool CastTo<T>(ref T target)
  {
    var type = typeof(T);
    if (type == typeof(IGH_GeometricGoo))
    {
      target = (T)(object)GH_Convert.ToGeometricGoo(Value.GeometryBase);
      return true;
    }

    // TODO: cast to material, etc.

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

  public BoundingBox ClippingBox => Value.GeometryBase.GetBoundingBox(false);

  public SpeckleObjectGoo(SpeckleObject value)
  {
    Value = value;
  }

  public SpeckleObjectGoo() { }
}

public class SpeckleObjectParam : GH_Param<SpeckleObjectGoo>
{
  public SpeckleObjectParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleObjectParam(GH_ParamAccess access)
    : base("Speckle Grasshopper Object", "SGO", "XXXXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("22FD5510-D5D3-4101-8727-153FFD329E4F");
}
