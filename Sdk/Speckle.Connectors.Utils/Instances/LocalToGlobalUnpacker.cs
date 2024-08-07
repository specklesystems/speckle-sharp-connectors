using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;
using Speckle.DoubleNumerics;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Utils.Instances;

/// <summary>
/// Utility for the connectors that doesn't support instancing.
/// An atomic object can be seen on only one definition. We search matrices from bottom to top for each atomic object.
/// DI scope -> (InstancePerLifetimeScope).
/// </summary>
[GenerateAutoInterface]
public class LocalToGlobalUnpacker : ILocalToGlobalUnpacker
{
  public List<LocalToGlobalMap> Unpack(
    List<InstanceDefinitionProxy>? instanceDefinitionProxies,
    List<TraversalContext> objectsToUnpack
  )
  {
    var localToGlobalMaps = new List<LocalToGlobalMap>();

    var instanceProxies = new List<(TraversalContext tc, InstanceProxy obj)>();
    var atomicObjects = new List<(TraversalContext tc, Base obj)>();

    // 1. Split up the instances from the non-instances
    foreach (TraversalContext objectToUnpack in objectsToUnpack)
    {
      if (objectToUnpack.Current is InstanceProxy instanceProxy)
      {
        instanceProxies.Add((objectToUnpack, instanceProxy));
      }
      else
      {
        atomicObjects.Add((objectToUnpack, objectToUnpack.Current));
      }
    }

    var objectsAtAbsolute = new List<(TraversalContext tc, Base obj)>();
    var objectsAtRelative = new List<(TraversalContext tc, Base obj)>();

    // 2. Split atomic objects that in absolute or relative coordinates.
    foreach ((TraversalContext tc, Base atomicObject) in atomicObjects)
    {
      if (atomicObject.applicationId is null)
      {
        continue;
      }
      if (
        instanceDefinitionProxies is not null
        && instanceDefinitionProxies.Any(idp => idp.objects.Contains(atomicObject.applicationId))
      )
      {
        objectsAtRelative.Add((tc, atomicObject)); // to use in Instances only
      }
      else
      {
        objectsAtAbsolute.Add((tc, atomicObject)); // to bake
      }
    }

    // 3. Add atomic objects that on absolute coordinates that doesn't need a transformation.
    foreach ((TraversalContext tc, Base objectAtAbsolute) in objectsAtAbsolute)
    {
      localToGlobalMaps.Add(new LocalToGlobalMap(tc, objectAtAbsolute, new List<Matrix4x4>()));
    }

    // 4. Return if no logic around instancing.
    if (instanceDefinitionProxies is null)
    {
      return localToGlobalMaps;
    }

    // 5. Iterate each object that in relative coordinates.
    foreach (var objectAtRelative in objectsAtRelative)
    {
      UnpackMatrix(
        instanceDefinitionProxies,
        instanceProxies,
        objectAtRelative,
        objectAtRelative,
        new List<Matrix4x4>(),
        localToGlobalMaps
      );
    }

    return localToGlobalMaps.Where(ltgm => ltgm.AtomicObject is not InstanceProxy).ToList();
  }

  private void UnpackMatrix(
    List<InstanceDefinitionProxy> instanceDefinitionProxies,
    List<(TraversalContext tc, InstanceProxy instanceProxy)> instanceProxies,
    (TraversalContext tc, Base obj) objectAtRelative,
    (TraversalContext tc, Base obj) searchForDefinition,
    List<Matrix4x4> matrices,
    List<LocalToGlobalMap> localToGlobalMaps
  )
  {
    if (searchForDefinition.obj.applicationId is null)
    {
      return;
    }
    InstanceDefinitionProxy? definitionProxy = instanceDefinitionProxies.Find(idp =>
      idp.objects.Contains(searchForDefinition.obj.applicationId)
    );
    if (definitionProxy is null)
    {
      localToGlobalMaps.Add(
        new LocalToGlobalMap(
          new TraversalContext(objectAtRelative.obj, objectAtRelative.tc.PropName, objectAtRelative.tc.Parent),
          objectAtRelative.obj,
          matrices
        )
      );
      return;
    }
    var instances = instanceProxies.Where(ic => ic.instanceProxy.definitionId == definitionProxy.applicationId);
    foreach (var instance in instances)
    {
      List<Matrix4x4> newMatrices = [.. matrices, instance.instanceProxy.transform]; // Do not mutate the list!
      UnpackMatrix(
        instanceDefinitionProxies,
        instanceProxies,
        objectAtRelative,
        instance,
        newMatrices,
        localToGlobalMaps
      );
    }
  }
}
