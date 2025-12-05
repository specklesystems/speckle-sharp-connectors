#if RHINO8_OR_GREATER
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Grasshopper.Rhinoceros.Model;
using Rhino.DocObjects;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// Rhino 8+ ModelObject casting support for SpeckleDataObjectWrapperGoo.
/// </summary>
public partial class SpeckleDataObjectWrapperGoo : GH_Goo<SpeckleDataObjectWrapper>, IGH_PreviewData
{
  /// <summary>
  /// Handles casting from Rhino 8+ ModelObjects to DataObject.
  /// </summary>
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      case ModelObject modelObject:
        var geometryWrapperGoo = new SpeckleGeometryWrapperGoo();
        if (geometryWrapperGoo.CastFrom(modelObject))
        {
          return CastFromSpeckleGeometryWrapper(geometryWrapperGoo.Value);
        }
        return false;

      case RhinoObject rhinoObject: // can this happen? I'm inclined to say no, but who knows with gh
        return CastFromModelObject((ModelObject)rhinoObject);

      default:
        return false;
    }
  }

  /// <summary>
  /// Handles casting from DataObject to Rhino 8+ ModelObjects.
  /// </summary>
  /// <remarks>
  /// Only works if DataObject has exactly one geometry
  /// </remarks>
  private bool CastToModelObject<T>(ref T target)
  {
    if (Value.Geometries.Count != 1)
    {
      return false;
    }

    // extract first (and only) geometry and delegate to SpeckleGeometryWrapperGoo
    var firstGeometry = Value.Geometries[0];
    var geometryGoo = new SpeckleGeometryWrapperGoo(firstGeometry);

    // using existing ModelObject casting logic from SpeckleGeometryWrapperGoo
    return geometryGoo.CastTo(ref target);
  }
}
#endif
