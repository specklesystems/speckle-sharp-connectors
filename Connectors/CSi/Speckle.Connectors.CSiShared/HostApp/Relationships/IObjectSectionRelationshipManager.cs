using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Relationships;

/// <summary>
/// Manages relationships between converted objects and their assigned sections.
/// </summary>
/// <remarks>
/// Separated from section-material relationships for clearer responsibility boundaries.
/// Handles mapping between elements and their section assignments.
/// </remarks>
public interface IObjectSectionRelationshipManager
{
  /// <summary>
  /// Establishes relationships between converted objects and their section proxies.
  /// </summary>
  void EstablishRelationships(
    List<Base> convertedObjectsByType,
    IReadOnlyDictionary<string, IProxyCollection> sections
  );
}
