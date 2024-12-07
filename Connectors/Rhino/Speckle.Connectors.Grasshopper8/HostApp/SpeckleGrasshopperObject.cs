using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.HostApp;

public class SpeckleGrasshopperObject : Base
{
  public Base OriginalObject { get; set; }
  public GeometryBase GeometryBase { get; set; }
  public List<Collection> Path { get; set; }

  // RenderMaterial, ColorProxies, Properties (?)
  public override string ToString() => $"Speckle Wrapper [{GeometryBase.GetType().Name}]";
}

public class SpeckleGrasshopperObjectGoo : GH_Goo<SpeckleGrasshopperObject>
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Object [{m_value.OriginalObject.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle object wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper objects.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleGrasshopperObject speckleGrasshopperObject:
        Value = speckleGrasshopperObject;
        return true;
      case GH_Goo<SpeckleGrasshopperObject> speckleGrasshopperObjectGoo:
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

    if (type == typeof(GH_Extrusion) && Value.GeometryBase is Extrusion)
    {
      target = (T)(object)new GH_Extrusion() { Value = Value.GeometryBase as Extrusion };
      return true;
    }
    return false;
  }
}

public class SpeckleGrasshopperObjectParam : GH_Param<SpeckleGrasshopperObjectGoo>
{
  public SpeckleGrasshopperObjectParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleGrasshopperObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleGrasshopperObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleGrasshopperObjectParam(GH_ParamAccess access)
    : base("Speckle Grasshopper Object", "SGO", "XXXXX", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("22FD5510-D5D3-4101-8727-153FFD329E4F");
}
