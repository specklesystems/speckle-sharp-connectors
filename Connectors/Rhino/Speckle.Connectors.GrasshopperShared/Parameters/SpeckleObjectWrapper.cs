using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a geometry base object and its converted speckle equivalent.
/// </summary>
public class SpeckleObjectWrapper : SpeckleWrapper
{
  public override required Base Base { get; set; }

  /// <summary>
  /// The GeometryBase corresponding to the <see cref="SpeckleWrapper.Base"/>
  /// </summary>
  /// <remarks>
  /// POC: how will we send intervals and other gh native objects? do we? maybe not for now?
  /// Objects using fallback conversion (eg DataObjects) will create one wrapper per geometry in the display value.
  /// </remarks>
  public required GeometryBase? GeometryBase { get; set; }

  // The list of layer/collection names that forms the full path to this object
  public List<string> Path { get; set; } = new();
  public SpeckleCollectionWrapper? Parent { get; set; }
  public SpecklePropertyGroupGoo Properties { get; set; } = new();

  /// <summary>
  /// The color of the <see cref="Base"/>
  /// </summary>
  public Color? Color { get; set; }

  /// <summary>
  /// The material of the <see cref="Base"/>
  /// </summary>
  public SpeckleMaterialWrapper? Material { get; set; }

  /// <summary>
  /// Represents the guid of this <see cref="SpeckleObjectWrapper"/>
  /// </summary>
  /// <remarks>This property will usually be assigned in create components, or in publish components, and may differ from <see cref="Base.applicationId"/></remarks>
  public required string? WrapperGuid { get; set; }

  public override string ToString() => $"Speckle Wrapper [{GeometryBase?.GetType().Name}]";

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
        args.Display.DrawExtrusionWires(e, isSelected ? args.WireColour_Selected : args.WireColour);
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

      if (bakeLayerIndex < 0)
      {
        return;
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
      int matIndex = materialWrapper.Bake(doc, materialWrapper.Name);
      if (matIndex >= 0)
      {
        att.MaterialIndex = matIndex;
        att.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }
    }

    // add props
    Properties.AssignToObjectAttributes(att);

    // add to doc
    Guid guid = doc.Objects.Add(GeometryBase, att);
    obj_ids.Add(guid);
  }

  /// <summary>
  /// Determines similarity of two SpeckleObjectWrappers.
  /// If the path, name, and properties of the wrappers are the same, they should be considered similar.
  /// This should be used to pack similar objects into one `DataObject` on send.
  /// </summary>
  /// <param name="objWrapper">The object wrapper to compare to</param>
  /// <returns></returns>
  /// <remarks> Application Id is not considered in similarity, because these can be unique to objects inside the same displayvalue for proxy reasons</remarks>
  public bool SmellsLike(SpeckleObjectWrapper objWrapper)
  {
    if (Path != objWrapper.Path)
    {
      return false;
    }

    if (Name != objWrapper.Name)
    {
      return false;
    }

    /*
    if (!Properties.Equals(objWrapper.Properties))
    {
      return false;
    }
    */

    return true;
  }

  public SpeckleObjectWrapper DeepCopy() =>
    new()
    {
      Base = Base.ShallowCopy(),
      GeometryBase = GeometryBase?.Duplicate(),
      Color = Color,
      Material = Material,
      WrapperGuid = WrapperGuid,
      ApplicationId = ApplicationId,
      Parent = Parent,
      Properties = Properties,
      Name = Name,
      Path = Path
    };
}

public partial class SpeckleObjectWrapperGoo : GH_Goo<SpeckleObjectWrapper>, IGH_PreviewData
{
  public override IGH_Goo Duplicate()
  {
    return new SpeckleObjectWrapperGoo(Value.DeepCopy());
  }

  public override string ToString() => $@"Speckle Object Goo [{m_value.Base.speckle_type}]";

  public override bool IsValid => true;
  public override string TypeName => "Speckle object wrapper";
  public override string TypeDescription => "A wrapper around speckle grasshopper objects.";

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
      case GH_Goo<SpeckleObjectWrapper> speckleGrasshopperObjectGoo:
        Value = speckleGrasshopperObjectGoo.Value.DeepCopy();
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
          Material = null,
          WrapperGuid = null,
          ApplicationId = Guid.NewGuid().ToString()
        };
        return true;
    }

    // Handle case of model objects in rhino 8
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
    var circle = new Rhino.Geometry.Circle();
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

  public SpeckleObjectWrapperGoo(SpeckleObjectWrapper value)
  {
    Value = value;
  }

  public SpeckleObjectWrapperGoo()
  {
    Value = new()
    {
      Base = new(),
      GeometryBase = null,
      Color = null,
      Material = null,
      WrapperGuid = null,
    };
  }
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

  protected override Bitmap Icon => Resources.speckle_param_object;

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

  /// <summary>
  /// Bakes the object
  /// </summary>
  /// <param name="doc"></param>
  /// <param name="att"></param>
  /// <param name="obj_ids"></param>
  /// <remarks>
  /// The attributes come from the user dialog after calling bake.
  /// The selected layer from the dialog will only be user if no path is already present on the object.
  /// </remarks>
  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleObjectWrapperGoo goo)
      {
        int layerIndex = goo.Value.Path.Count == 0 ? att.LayerIndex : -1;
        bool layerCreated = goo.Value.Path.Count == 0;
        goo.Value.Bake(doc, obj_ids, layerIndex, layerCreated);
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
        if (item is SpeckleObjectWrapperGoo goo && goo.Value.GeometryBase is GeometryBase gb)
        {
          var box = gb.GetBoundingBox(false);
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
