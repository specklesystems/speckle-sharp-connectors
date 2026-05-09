using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Sdk.Models;
using DataObject = Speckle.Objects.Data.DataObject;

namespace Speckle.Connectors.GrasshopperShared.Operations.Send;

/// <summary>
/// Shared unwrap logic used by both send paths (standard and continuous traversal).
/// </summary>
internal static class GrasshopperSendUnwrapper
{
  /// <summary>
  /// Converts a geometry wrapper to its underlying Speckle <see cref="Base"/>.
  /// When Rhino geometry is available, reconverts from Rhino to get a clean Speckle object.
  /// </summary>
  /// <remarks>
  /// Saw some IFC objects arrive with @-prefix chunked arrays (e.g. <c>@vertices_5000_0</c>) that don't map
  /// to typed properties on deserialization — publishing them as-is crashes the mesh converter on re-receive
  /// (See CNX-3303).
  /// </remarks>
  public static Base UnwrapGeometry(SpeckleGeometryWrapper wrapper)
  {
    Dictionary<string, object?> props = [];
    Base baseObject = wrapper.Base;

    if (wrapper.GeometryBase != null)
    {
      var reconverted = SpeckleConversionContext.Current.ConvertToSpeckle(wrapper.GeometryBase);
      if (reconverted != null)
      {
        reconverted.applicationId = wrapper.Base.applicationId;
        if (wrapper.Base["name"] is string name)
        {
          reconverted["name"] = name;
        }
        baseObject = reconverted;
      }
    }

    if (wrapper.Properties.CastTo(ref props))
    {
      baseObject["properties"] = props;
    }

    return baseObject;
  }

  /// <summary>
  /// Converts a data object wrapper to a clean <see cref="DataObject"/> with its display value set.
  /// Builds a fresh DataObject rather than mutating the original.
  /// </summary>
  /// <remarks> 
  /// IFC objects carry structural dynamic properties (e.g. <c>elements</c>) that inflate object counts when traversed
  /// on re-receive (See CNX-3303). 
  /// </remarks>
  public static DataObject UnwrapDataObject(
    SpeckleDataObjectWrapper wrapper,
    GrasshopperColorPacker colorPacker,
    GrasshopperMaterialPacker materialPacker
  )
  {
    var displayValue = new List<Base>();
    foreach (var geometryWrapper in wrapper.Geometries)
    {
      displayValue.Add(UnwrapGeometry(geometryWrapper));

      if (geometryWrapper.ApplicationId != null)
      {
        colorPacker.ProcessColor(geometryWrapper.ApplicationId, geometryWrapper.Color);
        materialPacker.ProcessMaterial(geometryWrapper.ApplicationId, geometryWrapper.Material);
      }
    }

    return new DataObject
    {
      applicationId = wrapper.DataObject.applicationId,
      name = wrapper.DataObject.name,
      properties = wrapper.DataObject.properties,
      displayValue = displayValue,
    };
  }
}
