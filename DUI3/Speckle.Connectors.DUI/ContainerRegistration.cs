using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Threading;
using Speckle.Sdk;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.DUI;

public static class ContainerRegistration
{
  public static void AddDUI<TThreadContext>(this IServiceCollection serviceCollection)
    where TThreadContext : IThreadContext, new()
  {
    // send operation and dependencies
    serviceCollection.AddSingleton<IThreadContext>(new TThreadContext());
    serviceCollection.AddSingleton<IRootObjectSender, RootObjectSender>();
    serviceCollection.AddTransient<IBrowserBridge, BrowserBridge>(); // POC: Each binding should have it's own bridge instance

    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)));
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IServerTransportFactory)));
  }

  public static void RegisterTopLevelExceptionHandler(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddSingleton<IBinding, TopLevelExceptionHandlerBinding>(sp =>
      sp.GetRequiredService<TopLevelExceptionHandlerBinding>()
    );
    serviceCollection.AddSingleton<TopLevelExceptionHandlerBinding>();
    serviceCollection.AddSingleton<ITopLevelExceptionHandler>(c =>
      c.GetRequiredService<TopLevelExceptionHandlerBinding>().Parent.TopLevelExceptionHandler
    );
  }
}
