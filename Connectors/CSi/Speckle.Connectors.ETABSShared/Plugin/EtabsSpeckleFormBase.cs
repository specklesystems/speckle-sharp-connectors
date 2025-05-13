using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.CSiShared;

namespace Speckle.Connectors.ETABSShared;

public abstract class EtabsSpeckleFormBase : SpeckleFormBase
{
  protected override Speckle.Sdk.Application GetHostApplication() => HostApplications.ETABS;

  protected override void ConfigureServices(IServiceCollection services)
  {
    base.ConfigureServices(services);
    services.AddEtabs();
  }
}
