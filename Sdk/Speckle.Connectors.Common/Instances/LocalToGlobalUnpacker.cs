using Speckle.DoubleNumerics;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Data;
using Speckle.Sdk.Dependencies;
using Speckle.Sdk.Models;
using Speckle.Sdk.Models.GraphTraversal;
using Speckle.Sdk.Models.Instances;

namespace Speckle.Connectors.Common.Instances;

/// <summary>
/// Utility for the connectors that doesn't support instancing.
/// An atomic object can be seen on only one definition. We search matrices from bottom to top for each atomic object.
/// DI scope -> (InstancePerLifetimeScope).
/// </summary>
[GenerateAutoInterface]
public class LocalToGlobalUnpacker : ILocalToGlobalUnpacker
{
  public IReadOnlyCollection<LocalToGlobalMap> Unpack(
    IReadOnlyCollection<InstanceDefinitionProxy>? instanceDefinitionProxies,
    IReadOnlyCollection<TraversalContext> objectsToUnpack
  )
  {
    var localToGlobalMaps = new HashSet<LocalToGlobalMap>();

    var instanceProxies = new HashSet<(TraversalContext tc, InstanceProxy obj)>();
    var atomicObjects = new HashSet<(TraversalContext tc, Base obj)>();

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

      if (objectToUnpack.Current is DataObject dataObject)
      {
        foreach (Base displayValue in dataObject.displayValue)
        {
          if (displayValue is InstanceProxy instanceProxyInDisplayValue)
          {
            instanceProxies.Add(
              (new TraversalContext(instanceProxyInDisplayValue, parent: objectToUnpack), instanceProxyInDisplayValue)
            );
          }
        }
      }
    }

    var objectsAtAbsolute = new HashSet<(TraversalContext tc, Base obj)>();
    var objectsAtRelative = new HashSet<(TraversalContext tc, Base obj)>();

    // 2. Split atomic objects that in absolute or relative coordinates.
    foreach ((TraversalContext tc, Base atomicObject) in atomicObjects)
    {
      // If we have an application id, and it's part of an instance -> go through the relative process
      if (
        atomicObject.applicationId is not null
        && instanceDefinitionProxies is not null
        && instanceDefinitionProxies.Any(idp => idp.objects.Contains(atomicObject.applicationId))
      )
      {
        objectsAtRelative.Add((tc, atomicObject)); // to use in Instances only
        continue;
      }

      // Otherwise we're on the absolute track
      objectsAtAbsolute.Add((tc, atomicObject));
    }

    // 3. Add atomic objects that on absolute coordinates that doesn't need a transformation.
    foreach ((TraversalContext tc, Base objectAtAbsolute) in objectsAtAbsolute)
    {
      localToGlobalMaps.Add(new LocalToGlobalMap(tc, objectAtAbsolute, new HashSet<Matrix4x4>()));
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
        new HashSet<Matrix4x4>(),
        localToGlobalMaps
      );
    }

    return localToGlobalMaps.Where(ltgm => ltgm.AtomicObject is not InstanceProxy).Freeze();
  }

  private void UnpackMatrix(
    IReadOnlyCollection<InstanceDefinitionProxy> instanceDefinitionProxies,
    HashSet<(TraversalContext tc, InstanceProxy instanceProxy)> instanceProxies,
    (TraversalContext tc, Base obj) objectAtRelative,
    (TraversalContext tc, Base obj) searchForDefinition,
    HashSet<Matrix4x4> matrices,
    HashSet<LocalToGlobalMap> localToGlobalMaps
  )
  {
    if (searchForDefinition.obj.applicationId is null)
    {
      return;
    }
    InstanceDefinitionProxy? definitionProxy = instanceDefinitionProxies.FirstOrDefault(idp =>
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
      HashSet<Matrix4x4> newMatrices = [.. matrices, instance.instanceProxy.transform]; // Do not mutate the list!
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
