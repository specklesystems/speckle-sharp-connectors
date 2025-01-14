using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

public interface IProxyRelationshipManager
{
  /// <summary>
  /// Manages relationships between sections, materials, and converted objects.
  /// </summary>
  /// <remarks>
  /// Centralizes relationship management to maintain clear separation of concerns.
  /// Operates on collections of proxies and objects after initial conversion.
  /// Assumes objects have already been converted and organized by type.
  /// </remarks>
  void EstablishRelationships(
    Dictionary<string, List<Base>> convertedObjectsByType,
    List<IProxyCollection> materialProxies,
    List<IProxyCollection> sectionProxies
  );
}
