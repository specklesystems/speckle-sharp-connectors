using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp;

public interface ISectionUnpacker
{
  /// <summary>
  /// Defines contract for unpacking section properties from CSi products and creating section proxies.
  /// </summary>
  /// <remarks>
  /// Base implementation provides common CSi properties with application-specific implementations
  /// adding their own properties through method overrides.
  /// Follows template method pattern for extensibility.
  /// </remarks>
  List<IProxyCollection> UnpackSections(Collection rootObjectCollection);
}

public interface IFrameSectionUnpacker
{
  /// <summary>
  /// Gets assigned material name for given section.
  /// </summary>
  string? GetAssignedMaterialName(string sectionName);
}
