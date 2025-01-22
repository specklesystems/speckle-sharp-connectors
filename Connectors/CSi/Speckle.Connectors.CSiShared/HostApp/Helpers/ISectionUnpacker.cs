using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

public interface ISectionUnpacker
{
  IEnumerable<GroupProxy> UnpackSections();
}
