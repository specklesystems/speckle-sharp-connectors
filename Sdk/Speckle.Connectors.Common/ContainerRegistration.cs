using System.Reflection;
using Autofac;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;

namespace Speckle.Connectors.Common;

public static class ContainerRegistration
{
  public static void AddConnectorUtils(this SpeckleContainerBuilder builder)
  {
    // send operation and dependencies
    builder.AddSingleton<CancellationManager>();
    builder.AddScoped<RootObjectUnpacker>();
    builder.AddScoped<ReceiveOperation>();
    builder.AddSingleton<AccountService>();
    builder.ScanAssembly(Assembly.GetExecutingAssembly());

    builder.ContainerBuilder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
  }
}
