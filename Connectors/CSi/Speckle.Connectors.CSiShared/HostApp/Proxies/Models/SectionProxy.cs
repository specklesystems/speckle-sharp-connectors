using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

/// <summary>
/// Represents a section proxy with properties, material reference, and object references in a CSi model.
/// </summary>
/// <remarks>
/// Section properties combine common CSi properties with application-specific extensions.
/// Objects list contains references to elements using this section.
/// MaterialName is required to establish material-section relationships.
/// </remarks>

[SpeckleType("Objects.Other.SectionProxy")]
public class SectionProxy : Base, IProxyCollection
{
  public List<string> objects { get; set; } = [];
  public Dictionary<string, object?> Properties { get; set; } = []; // What's the convention here? camelCase?
  public required string MaterialName { get; init; } // Required property for relationships
}
