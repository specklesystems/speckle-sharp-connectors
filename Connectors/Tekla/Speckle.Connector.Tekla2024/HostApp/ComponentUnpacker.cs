using Speckle.Converter.Tekla2024.Extensions;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connector.Tekla2024.HostApp;

public class ComponentUnpacker
{
  // POC: should add ILogger here in the case that component unpacker fails to unpack a component

  /// <summary>
  /// Stores processed Base Components as group proxies. These include Components and Connections.
  /// Expects to be scoped per send operation. Should be added to the root collection.
  /// </summary>
  public Dictionary<string, GroupProxy> ComponentProxiesCache { get; } = new();

  public ComponentUnpacker() { }

  public IEnumerable<TSM.ModelObject> UnpackComponents(IReadOnlyList<TSM.ModelObject> modelObjects)
  {
    foreach (TSM.ModelObject modelObject in modelObjects)
    {
      if (modelObject is TSM.BaseComponent component)
      {
        // create a group proxy for this component
        string appId = component.GetSpeckleApplicationId();
        List<string> childIds = new();

        foreach (TSM.ModelObject child in component.GetChildren())
        {
          childIds.Add(child.GetSpeckleApplicationId());
          yield return child;
        }

        GroupProxy componentProxy =
          new()
          {
            name = component.Name,
            objects = childIds,
            applicationId = appId
          };

        componentProxy["number"] = component.Number;

        if (!ComponentProxiesCache.ContainsKey(appId))
        {
          ComponentProxiesCache.Add(appId, componentProxy);
        }
      }
      else
      {
        yield return modelObject;
      }
    }
  }
}
