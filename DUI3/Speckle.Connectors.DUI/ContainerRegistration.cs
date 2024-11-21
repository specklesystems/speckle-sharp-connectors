using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.DUI;

public static class ContainerRegistration
{ /*
  public static void AddDUI(this SpeckleContainerBuilder speckleContainerBuilder)
  {
    // send operation and dependencies
    speckleContainerBuilder.AddSingletonInstance<ISyncToThread, SyncToUIThread>();
    speckleContainerBuilder.AddSingleton<IRootObjectSender, RootObjectSender>();
    speckleContainerBuilder.AddTransient<IBrowserBridge, BrowserBridge>(); // POC: Each binding should have it's own bridge instance
    speckleContainerBuilder.AddSingleton(GetJsonSerializerSettings());
    speckleContainerBuilder.ScanAssemblyOfType<IdleCallManager>();
    speckleContainerBuilder.ScanAssemblyOfType<IServerTransportFactory>();
  }
*/
  public static void AddDUI(this IServiceCollection serviceCollection)
  {
    // send operation and dependencies
    serviceCollection.AddSingleton<ISyncToThread, SyncToUIThread>();
    serviceCollection.AddSingleton<IRootObjectSender, RootObjectSender>();
    serviceCollection.AddTransient<IBrowserBridge, BrowserBridge>(); // POC: Each binding should have it's own bridge instance
    serviceCollection.AddSingleton(sp => sp.GetRequiredService<IJsonSerializerSettingsFactory>().Create());

    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)));
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IServerTransportFactory)));
  }

  public static void UseDUI(this IServiceProvider serviceProvider)
  {
    serviceProvider.GetRequiredService<ISyncToThread>();
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
