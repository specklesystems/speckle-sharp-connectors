using System.Reflection;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a geometry base object and its converted speckle equivalent.
/// </summary>
public class SpeckleObjectWrapper : Base
{
  public required Base Base { get; set; }

  // note: how will we send intervals and other gh native objects? do we? maybe not for now
  // note: this does not handle on to many relationship well.
  // For receiving data objects, we are wrapping every value in the data object display value, and storing a reference to the same data object in each wrapped object.
  public required GeometryBase GeometryBase { get; set; }

  // The list of layer/collection names that forms the full path to this object
  public List<string> Path { get; set; } = new();
  public SpeckleCollectionWrapper? Parent { get; set; }

  // A dictionary of property path to property
  public SpecklePropertyGroupGoo Properties { get; set; } = new();
  public required string Name { get; set; } = "";
  public required Color? Color { get; set; }
  public required SpeckleMaterialWrapper? Material { get; set; }

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

      case Rhino.Geometry.Point p:
        args.Display.DrawPoint(p.Location, isSelected ? args.WireColour_Selected : args.WireColour);
        break;

      case PointCloud pc:
        args.Display.DrawPointCloud(pc, 1, isSelected ? args.WireColour_Selected : args.WireColour);
        break;

      case Hatch h:
        args.Display.DrawHatch(
          h,
          isSelected ? args.WireColour_Selected : args.WireColour,
          isSelected ? args.WireColour_Selected : args.WireColour
        );
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
        var eBrep = e.ToBrep();
        display.DrawBrepShaded(eBrep, material);
        display.DrawBrepWires(eBrep, material.Diffuse);
        break;
      case SubD d:
        display.DrawSubDShaded(d, material);
        display.DrawSubDWires(d, material.Diffuse, display.DefaultCurveThickness);
        break;
      case Curve c:
        display.DrawCurve(c, material.Diffuse);
        break;
      case Rhino.Geometry.Point p:
        display.DrawPoint(p.Location, material.Diffuse);
        break;
      case PointCloud pc:
        display.DrawPointCloud(pc, 1, material.Diffuse);
        break;
      case Hatch h:
        display.DrawHatch(h, material.Diffuse, material.Diffuse);
        break;
    }
  }

  public void Bake(RhinoDoc doc, List<Guid> obj_ids, int bakeLayerIndex = -1, bool layersAlreadyCreated = false)
  {
    // get or make layers
    if (!layersAlreadyCreated && bakeLayerIndex < 0)
    {
      if (Path.Count > 0 && Parent != null)
      {
        bakeLayerIndex = Parent.Bake(doc, obj_ids, false);
      }
    }

    // create attributes
    using ObjectAttributes att = new() { Name = Name, LayerIndex = bakeLayerIndex };

    if (Color is Color color)
    {
      att.ObjectColor = color;
      att.ColorSource = ObjectColorSource.ColorFromObject;
    }

    if (Material is SpeckleMaterialWrapper materialWrapper)
    {
      int matIndex = materialWrapper.Bake(doc, materialWrapper.Base.name);
      if (matIndex >= 0)
      {
        att.MaterialIndex = matIndex;
        att.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }
    }

    foreach (var kvp in Properties.Value)
    {
      att.SetUserString(kvp.Key, kvp.Value.Value.ToString());
    }

    // add to doc
    Guid guid = doc.Objects.Add(GeometryBase, att);
    obj_ids.Add(guid);
  }
}

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData, ISpeckleGoo
{
  public override IGH_Goo Duplicate() => throw new NotImplementedException();

  public override string ToString() => $@"Speckle Object Goo [{m_value.Base?.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle object wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper objects.";

  public override bool CastFrom(object source)
  {
    switch (source)
    {
      case SpeckleObjectWrapper speckleGrasshopperObject:
        Value = speckleGrasshopperObject;
        return true;
      case GH_Goo<SpeckleObjectWrapper> speckleGrasshopperObjectGoo:
        Value = speckleGrasshopperObjectGoo.Value;
        return true;
      case IGH_GeometricGoo geometricGoo:
        var gooGB = geometricGoo.GeometricGooToGeometryBase();
        var gooConverted = SpeckleConversionContext.ConvertToSpeckle(gooGB);
        Value = new SpeckleObjectWrapper()
        {
          GeometryBase = gooGB,
          Base = gooConverted,
          Name = "",
          Color = null,
          Material = null
        };
        return true;
    }

    // Handle case of model objects in rhino 8
    return CastFromModelObject(source);
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;
#endif

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

  BoundingBox IGH_PreviewData.ClippingBox => Value.GeometryBase.GetBoundingBox(false);

  public SpeckleObjectWrapperGoo(SpeckleObjectWrapper value)
  {
    Value = value;
  }

  public SpeckleObjectWrapperGoo() { }
}

public class SpeckleObjectParam : GH_Param<SpeckleObjectWrapperGoo>, IGH_BakeAwareObject, IGH_PreviewObject
{
  public SpeckleObjectParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleObjectParam(GH_ParamAccess access)
    : base(
      "Speckle Object",
      "SO",
      "Represents a Speckle object",
      ComponentCategories.PRIMARY_RIBBON,
      ComponentCategories.PARAMETERS,
      access
    ) { }

  public override Guid ComponentGuid => new("22FD5510-D5D3-4101-8727-153FFD329E4F");

  protected override Bitmap? Icon
  {
    get
    {
      Assembly assembly = GetType().Assembly;
      var stream = assembly.GetManifestResourceStream(
        assembly.GetName().Name + "." + "Resources" + ".speckle_param_object.png"
      );
      return stream != null ? new Bitmap(stream) : null;
    }
  }

  public bool IsBakeCapable =>
    // False if no data
    !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleObjectWrapperGoo goo)
      {
        goo.Value.Bake(doc, obj_ids);
      }
    }
  }

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleObjectWrapperGoo goo)
      {
        goo.Value.Bake(doc, obj_ids);
      }
    }
  }

  public bool IsPreviewCapable => !VolatileData.IsEmpty;

  public BoundingBox ClippingBox
  {
    get
    {
      BoundingBox clippingBox = new();

      // Iterate over all data stored in the parameter
      foreach (var item in VolatileData.AllData(true))
      {
        if (item is SpeckleObjectWrapperGoo goo)
        {
          var box = goo.Value.GeometryBase.GetBoundingBox(false);
          clippingBox.Union(box);
        }
      }
      return clippingBox;
    }
  }
  bool IGH_PreviewObject.Hidden { get; set; }

  public void DrawViewportWires(IGH_PreviewArgs args)
  {
    // todo?
  }

  public void DrawViewportMeshes(IGH_PreviewArgs args)
  {
    var isSelected = args.Document.SelectedObjects().Contains(this);
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleObjectWrapperGoo goo)
      {
        goo.Value.DrawPreview(args, isSelected);
      }
    }
  }
}
