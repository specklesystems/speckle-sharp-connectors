using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;
using Speckle.Connectors.GrasshopperShared.Registration;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Wrapper around a geometry base object and its converted speckle equivalent.
/// </summary>
public class SpeckleGeometryWrapper : SpeckleWrapper, ISpeckleCollectionObject
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

  public override string ToString() =>
    $"Speckle Geometry : {(string.IsNullOrWhiteSpace(Name) ? Base.speckle_type : Name)}";

  public virtual void DrawPreview(IGH_PreviewArgs args, bool isSelected = false)
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

  public virtual void Bake(RhinoDoc doc, List<Guid> objIds, int bakeLayerIndex = -1, bool layersAlreadyCreated = false)
  {
    if (!layersAlreadyCreated && bakeLayerIndex < 0 && Path.Count > 0 && Parent != null)
    {
      bakeLayerIndex = Parent.Bake(doc, objIds, false);
      if (bakeLayerIndex < 0)
      {
        return;
      }
    }

    using var attributes = CreateObjectAttributes(bakeLayerIndex, true);
    Guid guid = doc.Objects.Add(GeometryBase, attributes);
    objIds.Add(guid);
  }

  public virtual SpeckleGeometryWrapper DeepCopy() =>
    new()
    {
      Base = (Base)Base.ShallowCopy(),
      GeometryBase = GeometryBase?.Duplicate(),
      Color = Color,
      Material = Material,
      ApplicationId = ApplicationId,
      Parent = Parent,
      Properties = Properties,
      Name = Name,
      Path = Path
    };

  public virtual ObjectAttributes CreateObjectAttributes(int layerIndex = -1, bool bakeMaterial = false)
  {
    var attributes = new ObjectAttributes { Name = Name };

    if (layerIndex >= 0)
    {
      attributes.LayerIndex = layerIndex;
    }

    AddColorToAttributes(attributes);
    AddMaterialToAttributes(attributes, bakeMaterial);
    AddPropertiesToAttributes(attributes);

    return attributes;
  }

  public override IGH_Goo CreateGoo() => new SpeckleGeometryWrapperGoo(this);

  protected virtual void AddPropertiesToAttributes(ObjectAttributes attributes) =>
    Properties?.AssignToObjectAttributes(attributes);

  protected virtual void AddColorToAttributes(ObjectAttributes attributes)
  {
    if (Color is Color validColor)
    {
      attributes.ObjectColor = validColor;
      attributes.ColorSource = ObjectColorSource.ColorFromObject;
    }
  }

  protected virtual void AddMaterialToAttributes(ObjectAttributes attributes, bool bakeMaterial)
  {
    if (Material is SpeckleMaterialWrapper materialWrapper && bakeMaterial)
    {
      // Only handle the baking scenario here
      // Existing baking logic from BakingHelpers (works in all Rhino versions)
      int matIndex = materialWrapper.Bake(CurrentDocument.Document.NotNull(), materialWrapper.Name);
      if (matIndex >= 0)
      {
        attributes.MaterialIndex = matIndex;
        attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
      }
    }

    // Note: bakeMaterial: false scenario (casting) is handled in ModelObjects.cs
    // where it belongs, with proper Rhino 8+ conditional compilation
  }
}
