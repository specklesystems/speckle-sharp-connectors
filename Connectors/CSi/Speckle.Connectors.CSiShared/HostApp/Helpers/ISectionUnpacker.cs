using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

public interface ISectionUnpacker
{
  IReadOnlyDictionary<string, IProxyCollection> UnpackSections(
    Collection rootObjectCollection,
    string[] frameSectionNames,
    string[] shellSectionNames
  );
}
