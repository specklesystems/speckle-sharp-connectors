#if RHINO8_OR_GREATER

namespace Speckle.Connectors.GrasshopperShared.Parameters;

public partial class SpeckleBlockInstanceWrapperGoo
{
  private bool CastFromModelObject(object source)
  {
    switch (source)
    {
      // TODO: Uncomment when block definitions are available
      // case ModelInstanceReference modelInstanceRef:
      //   return CastFromModelInstanceReference(modelInstanceRef);

      // case ModelInstanceDefinition modelInstanceDef:
      //   return CastFromModelInstanceDefinition(modelInstanceDef);

      default:
        return false;
    }
  }

  //private bool CastToModelObject<T>(ref T target)
  private bool CastToModelObject<T>(ref T _)
  {
    //var type = typeof(T);

    // TODO: Uncomment when block definitions are available
    // if (type == typeof(ModelInstanceReference))
    // {
    //   return CastToModelInstanceReference(ref target);
    // }

    return false;
  }

  // TODO: Uncomment and implement when block definitions are available
  // private bool CastFromModelInstanceReference(ModelInstanceReference modelInstanceRef)
  // {
  //   try
  //   {
  //     var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
  //     var definitionId = modelInstanceRef.InstanceDefinition?.Id?.ToString() ?? "unknown";
  //
  //     Value = new SpeckleBlockInstanceWrapper()
  //     {
  //       Base = new InstanceProxy()
  //       {
  //         definitionId = definitionId,
  //         maxDepth = 1,
  //         transform = GrasshopperHelpers.TransformToMatrix(modelInstanceRef.Transform, units),
  //         units = units,
  //         applicationId = modelInstanceRef.Id?.ToString() ?? Guid.NewGuid().ToString()
  //       },
  //       Transform = modelInstanceRef.Transform,
  //       ApplicationId = modelInstanceRef.Id?.ToString() ?? Guid.NewGuid().ToString()
  //     };
  //     return true;
  //   }
  //   catch (Exception ex) when (!ex.IsFatal())
  //   {
  //     return false;
  //   }
  // }

  // private bool CastFromModelInstanceDefinition(ModelInstanceDefinition modelInstanceDef)
  // {
  //   // Create an identity transform instance from just a definition
  //   var units = RhinoDoc.ActiveDoc?.ModelUnitSystem.ToSpeckleString() ?? "none";
  //
  //   Value = new SpeckleBlockInstanceWrapper()
  //   {
  //     Base = new InstanceProxy()
  //     {
  //       definitionId = modelInstanceDef.Id?.ToString() ?? "unknown",
  //       maxDepth = 1,
  //       transform = GrasshopperHelpers.TransformToMatrix(Transform.Identity, units),
  //       units = units,
  //       applicationId = Guid.NewGuid().ToString()
  //     },
  //     Transform = Transform.Identity,
  //     ApplicationId = Guid.NewGuid().ToString()
  //   };
  //   return true;
  // }

  // private bool CastToModelInstanceReference<T>(ref T target)
  // {
  //   // TODO: Create ModelInstanceReference from our instance data
  //   // Requires looking up the ModelInstanceDefinition and creating the reference
  //   return false;
  // }
}
#endif
