using Speckle.Sdk.Models.Proxies;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

// NOTE: Interface because Etabs and Sap2000 material unpacking and extraction is different.
// At ServiceRegistration, we inject the correct implementation of the IMaterialUnpacker
public interface IMaterialUnpacker
{
  IEnumerable<IProxyCollection> UnpackMaterials();
}
