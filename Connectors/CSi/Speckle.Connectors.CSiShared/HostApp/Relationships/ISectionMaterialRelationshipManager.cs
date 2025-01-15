using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Relationships;

/// <summary>
/// Manages relationships between sections and their assigned materials.
/// </summary>
/// <remarks>
/// Separated from object-section relationships for better separation of concerns.
/// Handles bidirectional relationships between material and section proxies.
/// </remarks>
public interface ISectionMaterialRelationshipManager
{
  /// <summary>
  /// Establishes bidirectional relationships between section and material proxies.
  /// </summary>
  void EstablishRelationships(List<IProxyCollection> sections, List<IProxyCollection> materials);
}
