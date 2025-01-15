using Speckle.Sdk.Models;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

/// <summary>
/// Represents a material proxy with properties and object references in a CSi model.
/// </summary>
/// <remarks>
/// Material properties follow CSi API organization with nested categories.
/// Objects list contains references to sections using this material.
/// Properties dictionary uses string keys matching CSi API terminology.
/// </remarks>

// TODO: These are currently not used - we're just using GroupProxy
[SpeckleType("Objects.Other.MaterialProxy")]
public class MaterialProxy : Base, IProxyCollection
{
  public List<string> objects { get; set; } = [];
  public Dictionary<string, object?>? Properties { get; set; } = [];
}
