using Autofac;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Cancellation;
using Speckle.Connectors.Utils.Operations;
using Speckle.Logging;

namespace Speckle.Connectors.Utils;

public static class ContainerRegistration
{
  public static void AddConnectorUtils(this SpeckleContainerBuilder builder)
  {
    // send operation and dependencies
    builder.AddSingleton<CancellationManager>();
    builder.AddScoped<ReceiveOperation>();
    builder.AddSingleton<AccountService>();
    builder.ScanAssemblyOfType<SendHelper>();

    builder.AddSingleton(SpeckleLog. GetLoggerFactory());
    builder.ContainerBuilder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
  }
}
