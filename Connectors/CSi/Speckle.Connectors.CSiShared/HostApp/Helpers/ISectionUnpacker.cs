using Speckle.Sdk.Models.Collections;
using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

public interface ISectionUnpacker
{
  List<IProxyCollection> UnpackSections(Collection rootObjectCollection);
}
