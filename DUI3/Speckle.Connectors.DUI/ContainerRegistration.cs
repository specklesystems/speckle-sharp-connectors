using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Testing;
using Speckle.Sdk;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.DUI;

public static class ContainerRegistration
{
  public static void AddDUI<TDocumentStore>(this IServiceCollection serviceCollection)
    where TDocumentStore : DocumentModelStore
  {
    serviceCollection.AddSingleton<DocumentModelStore, TDocumentStore>();
    serviceCollection.AddTesting();

    // send operation and dependencies
    serviceCollection.AddSingleton<ISyncToThread, SyncToUIThread>();
    serviceCollection.AddTransient<IBrowserBridge, BrowserBridge>(); // POC: Each binding should have it's own bridge instance

    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)));
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IServerTransportFactory)));
  }

  public static void UseDUI(this IServiceProvider serviceProvider) =>
    serviceProvider.GetRequiredService<ISyncToThread>();

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
