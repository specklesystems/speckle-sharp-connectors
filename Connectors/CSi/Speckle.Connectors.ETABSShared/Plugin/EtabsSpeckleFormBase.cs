using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.CSiShared;
using Speckle.Sdk.Host;

namespace Speckle.Connectors.ETABSShared;

public abstract class EtabsSpeckleFormBase : SpeckleFormBase
{
  protected override HostApplication GetHostApplication() => HostApplications.ETABS;

  protected override void ConfigureServices(IServiceCollection services)
  {
    base.ConfigureServices(services);
    services.AddETABS();
  }
}
