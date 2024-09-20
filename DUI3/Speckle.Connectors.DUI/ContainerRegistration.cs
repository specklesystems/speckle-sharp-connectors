using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Newtonsoft.Json;
using Speckle.Newtonsoft.Json.Serialization;
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
    serviceCollection.AddSingleton(GetJsonSerializerSettings());

    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)));
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IServerTransportFactory)));
  }

  public static void UseDUI(this IServiceProvider serviceProvider)
  {
    serviceProvider.GetRequiredService<ISyncToThread>();
  }

  private static JsonSerializerSettings GetJsonSerializerSettings()
  {
    // Register WebView2 panel stuff
    JsonSerializerSettings settings =
      new()
      {
        Error = (_, args) =>
        {
          // POC: we should probably do a bit more than just swallowing this!
          Console.WriteLine("*** JSON ERROR: " + args.ErrorContext);
        },
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate,
        Converters = { new DiscriminatedObjectConverter(), new AbstractConverter<DiscriminatedObject, ISendFilter>() }
      };
    return settings;
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
