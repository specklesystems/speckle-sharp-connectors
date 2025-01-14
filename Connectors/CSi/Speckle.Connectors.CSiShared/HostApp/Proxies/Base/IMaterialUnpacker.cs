using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

public interface IMaterialUnpacker
{
  /// <summary>
  /// Defines contract for unpacking material properties from CSi products and creating material proxies.
  /// </summary>
  /// <remarks>
  /// Assumes bulk extraction pattern where all materials are retrieved in a single operation.
  /// Properties are organized in a nested dictionary structure following CSi API organization.
  /// </remarks>
  List<IProxyCollection> UnpackMaterials(Collection rootObjectCollection);
}
