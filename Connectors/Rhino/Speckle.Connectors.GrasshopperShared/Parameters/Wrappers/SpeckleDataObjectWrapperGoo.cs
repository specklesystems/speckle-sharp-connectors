using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;
using Speckle.Sdk.Models;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Goo wrapper for SpeckleDataObjectWrapper.
/// </summary>
public partial class SpeckleDataObjectWrapperGoo : GH_Goo<SpeckleDataObjectWrapper>, IGH_PreviewData
{
  /// <summary>
  /// Creates goo with a DataObject wrapper.
  /// </summary>
  public SpeckleDataObjectWrapperGoo(SpeckleDataObjectWrapper value)
  {
    Value = value;
  }

  /// <summary>Parameterless constructor</summary>
  /// <remarks>Should only be used for casting!</remarks>
  public SpeckleDataObjectWrapperGoo()
  {
    Value = new()
    {
      Base = new DataObject
      {
        name = "",
        displayValue = [],
        properties = new Dictionary<string, object?>()
      },
      Geometries = []
    };
  }

  public override bool IsValid => Value?.DataObject is not null && Value.ApplicationId is not null;
  public override string TypeName => "Speckle Data Object";
  public override string TypeDescription => "Represents a Speckle data object";

  public override IGH_Goo Duplicate() => new SpeckleDataObjectWrapperGoo(Value.DeepCopy());

  public override string ToString() =>
    $"Speckle Data Object : {(string.IsNullOrWhiteSpace(Value.Name) ? Value.Base.speckle_type : Value.Name)}";

  /// <summary>
  /// Handles casting from other types to DataObject wrapper.
  /// </summary>
  public override bool CastFrom(object source)
  {
    switch (source)
    {
      // 1 - data object → data object
      case SpeckleDataObjectWrapper wrapper:
        Value = wrapper;
        return true;
      case SpeckleDataObjectWrapperGoo wrapperGoo:
        Value = wrapperGoo.Value;
        return true;

      // 2 - speckle geometry → data object
      case SpeckleBlockInstanceWrapper:
      case SpeckleBlockInstanceWrapperGoo:
        // TODO: We need to have a larger discussion around allowing instances within data objects.
        // We don't allow instances within data objects for now
        return false;
      case SpeckleGeometryWrapper geometryWrapper:
        return CastFromSpeckleGeometryWrapper(geometryWrapper);
      case SpeckleGeometryWrapperGoo geometryWrapperGoo:
        return CastFromSpeckleGeometryWrapper(geometryWrapperGoo.Value);

      // 3 - gh geometry → data object
      case IGH_GeometricGoo geometricGoo:
        return CastFromIghGeometricGoo(geometricGoo);

      // 4 - model object → data object (Rhino 8+)
      default:
        return CastFromModelObject(source); // Try ModelObject casting (will return false on Rhino 7)
    }
  }

  /// <summary>
  /// Handles casting to other types from DataObject wrapper.
  /// </summary>
  /// <remarks>
  /// Only allows geometry casting when DataObject has exactly one geometry.
  /// </remarks>
  public override bool CastTo<T>(ref T target)
  {
    switch (target)
    {
      case DataObject:
        target = (T)(object)Value.DataObject;
        return true;

      case SpeckleDataObjectWrapper:
        target = (T)(object)Value;
        return true;

      case SpeckleDataObjectWrapperGoo:
        target = (T)(object)this;
        return true;

      case Base:
        target = (T)(object)Value.DataObject;
        return true;

      // for geometry types, only allow if exactly one geometry
      default:
        if (Value.Geometries.Count == 1)
        {
          var singleGeometry = Value.Geometries[0];
          var geometryGoo = new SpeckleGeometryWrapperGoo(singleGeometry);

          // this should handle all IGH_GeometricGoo types and ModelObjects
          return geometryGoo.CastTo(ref target);
        }

        return CastToModelObject(ref target);
    }
  }

#if !RHINO8_OR_GREATER
  private bool CastFromModelObject(object _) => false;

  private bool CastToModelObject<T>(ref T _) => false;
#endif

  public void DrawViewportWires(GH_PreviewWireArgs args)
  {
    // TODO?
  }

  /// <summary>
  /// Draws viewport meshes/surfaces for the data object.
  /// </summary>
  public void DrawViewportMeshes(GH_PreviewMeshArgs args) => Value.DrawPreviewRaw(args.Pipeline, args.Material);

  /// <summary>
  /// Calculates the bounding box for all geometries in this data object.
  /// </summary>
  public BoundingBox ClippingBox
  {
    get
    {
      var clippingBox = new BoundingBox();

      foreach (var geometry in Value.Geometries)
      {
        if (geometry.GeometryBase != null)
        {
          var box = geometry.GeometryBase.GetBoundingBox(false);
          clippingBox.Union(box);
        }
      }

      return clippingBox;
    }
  }

  /// <summary>
  /// Creates a single-element DataObject from <see cref="SpeckleGeometryWrapper"/> (one geometry → one display value).
  /// </summary>
  private bool CastFromSpeckleGeometryWrapper(SpeckleGeometryWrapper geometryWrapper)
  {
    // create DataObject with single displayValue
    DataObject dataObject =
      new()
      {
        name = geometryWrapper.Name,
        displayValue = [geometryWrapper.Base],
        properties = geometryWrapper.Properties.Unwrap(),
        applicationId = geometryWrapper.ApplicationId
      };

    // create wrapper - Name, ApplicationId and Properties kept in sync with wrapped DataObject through getters/setters
    // geometry will inherit DataObject properties through the syncing (hopefully)
    Value = new SpeckleDataObjectWrapper
    {
      Base = dataObject,
      Geometries = [geometryWrapper],
      Path = [.. geometryWrapper.Path],
      Parent = geometryWrapper.Parent
    };

    return true;
  }

  private bool CastFromIghGeometricGoo(IGH_GeometricGoo geometricGoo)
  {
    SpeckleGeometryWrapperGoo geoGoo = new();
    if (geoGoo.CastFrom(geometricGoo))
    {
      // check if the geometry wrapper is valid before using it (CNX-2855)
      if (!geoGoo.IsValid)
      {
        return false;
      }
      return CastFromSpeckleGeometryWrapper(geoGoo.Value);
    }
    return false;
  }
}
