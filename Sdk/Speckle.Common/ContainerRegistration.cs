using Autofac;
using Microsoft.Extensions.Logging;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Common;

namespace Speckle.Connectors.Utils;

public static class ContainerRegistration
{
  public static void AddCommon(this SpeckleContainerBuilder builder)
  {
    builder.AddSingleton<ILoggerFactory>(new SpeckleLoggerFactory());
    builder.ContainerBuilder.RegisterGeneric(typeof(Logger<>)).As(typeof(ILogger<>)).SingleInstance();
    // send operation and dependencies
    builder.AddScoped<IUnitOfWorkFactory, UnitOfWorkFactory>();
  }
}
