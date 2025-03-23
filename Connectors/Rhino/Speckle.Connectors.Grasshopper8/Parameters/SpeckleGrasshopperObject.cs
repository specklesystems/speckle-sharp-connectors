using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.Grasshopper8.HostApp;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Collections;

namespace Speckle.Connectors.Grasshopper8.Parameters;

/// <summary>
/// Wrapper around a geometry base object and its converted speckle equivalent.
/// </summary>
public class SpeckleObject : Base
{
  public required Base Base { get; set; }
  public required GeometryBase GeometryBase { get; set; } // note: how will we send intervals and other gh native objects? do we? maybe not for now
  public List<Collection> Path { get; set; } = new();
  public Dictionary<string, string> UserStrings { get; set; } = new();
  public string Name { get; set; } = "";
  public int? Color { get; set; }
  public string? RenderMaterialName { get; set; }

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
        display.DrawMeshShaded(e.GetMesh(MeshType.Any), material);
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
}

public class SpeckleObjectGoo : GH_Goo<SpeckleObject>, IGH_PreviewData, ISpeckleGoo
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
      case SpeckleObject speckleGrasshopperObject:
        Value = speckleGrasshopperObject;
        return true;
      case GH_Goo<SpeckleObject> speckleGrasshopperObjectGoo:
        Value = speckleGrasshopperObjectGoo.Value;
        return true;
      case IGH_GeometricGoo geometricGoo:
        var gooGB = geometricGoo.GeometricGooToGeometryBase();
        var gooConverted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(gooGB);
        Value = new SpeckleObject() { GeometryBase = gooGB, Base = gooConverted };
        return true;
      case ModelObject modelObject:
        if (GetGeometryFromModelObject(modelObject) is GeometryBase modelGB)
        {
          var modelConverted = ToSpeckleConversionContext.ToSpeckleConverter.Convert(modelGB);
          SpeckleObject so =
            new()
            {
              GeometryBase = modelGB,
              Base = modelConverted,
              Name = modelObject.Name,
              Color = modelObject.Display.Color?.Color.ToArgb(),
              RenderMaterialName = modelObject.Render.Material?.Material?.Name,
              UserStrings = modelObject.UserText.ToDictionary(s => s.Key, s => s.Value)
            };
          Value = so;
          return true;
        }
        return false;
    }

    return false;
  }

  private GeometryBase? GetGeometryFromModelObject(ModelObject modelObject) =>
    RhinoDoc.ActiveDoc.Objects.FindId(modelObject.Id ?? Guid.Empty).Geometry;

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

  public void Bake(RhinoDoc doc, List<Guid> obj_ids)
  {
    // get or make layers
    int bakeLayerIndex = 0;
    if (Value.Path.Count > 0)
    {
      var layerBaker = new RhinoLayerBaker();

      var firstCollectionName = Value.Path.First().name;
      Collection[] childCollections = Value.Path.Skip(1).ToArray();
      layerBaker.CreateAllLayersForReceive(new List<Collection[]> { childCollections }, firstCollectionName);
      bakeLayerIndex = layerBaker.GetLayerIndex(childCollections, firstCollectionName);
    }

    // create attributes
    using ObjectAttributes att = new() { Name = Value.Name };

    if (Value.Color is int argb)
    {
      att.ObjectColor = Color.FromArgb(argb);
      att.ColorSource = ObjectColorSource.ColorFromObject;
      att.LayerIndex = bakeLayerIndex;
    }

    foreach (var kvp in Value.UserStrings)
    {
      att.SetUserString(kvp.Key, kvp.Value);
    }

    // add to doc
    Guid guid = doc.Objects.Add(Value.GeometryBase, att);
    obj_ids.Add(guid);
  }

  public BoundingBox ClippingBox => Value.GeometryBase.GetBoundingBox(false);

  public SpeckleObjectGoo(SpeckleObject value)
  {
    Value = value;
  }

  public SpeckleObjectGoo() { }
}

public class SpeckleObjectParam : GH_Param<SpeckleObjectGoo>, IGH_BakeAwareObject
{
  public SpeckleObjectParam()
    : this(GH_ParamAccess.item) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag)
    : base(tag) { }

  public SpeckleObjectParam(IGH_InstanceDescription tag, GH_ParamAccess access)
    : base(tag, access) { }

  public SpeckleObjectParam(GH_ParamAccess access)
    : base("Speckle Object", "SO", "Represents a Speckle object", "Speckle", "Params", access) { }

  public override Guid ComponentGuid => new("22FD5510-D5D3-4101-8727-153FFD329E4F");

  protected override Bitmap Icon => BitmapBuilder.CreateHexagonalBitmap("SO");

  public bool IsBakeCapable =>
    // False if no data
    !VolatileData.IsEmpty;

  public void BakeGeometry(RhinoDoc doc, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleObjectGoo goo)
      {
        goo.Bake(doc, obj_ids);
      }
    }
  }

  public void BakeGeometry(RhinoDoc doc, ObjectAttributes att, List<Guid> obj_ids)
  {
    // Iterate over all data stored in the parameter
    foreach (var item in VolatileData.AllData(true))
    {
      if (item is SpeckleObjectGoo goo)
      {
        goo.Bake(doc, obj_ids);
      }
    }
  }
}
