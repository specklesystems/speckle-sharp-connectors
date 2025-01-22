using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

// NOTE: Interface because Etabs and Sap2000 section unpacking and extraction is different.
// At ServiceRegistration, we inject the correct implementation of the ISectionUnpacker
public interface ISectionUnpacker
{
  IEnumerable<GroupProxy> UnpackSections();
}
