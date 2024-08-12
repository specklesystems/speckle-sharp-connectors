using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;

namespace Speckle.Connectors.Utils;

public static class ContainerRegistration
{
  public static void AddConnectors(this SpeckleContainerBuilder builder)
  {
    builder.AddCommon();
    // send operation and dependencies
    builder.AddSingleton<CancellationManager>();
    builder.AddScoped<ReceiveOperation>();
    builder.AddSingleton<AccountService>();
    builder.ScanAssemblyOfType<SendHelper>();
  }
}
