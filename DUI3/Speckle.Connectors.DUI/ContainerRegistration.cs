using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Sdk;
using Speckle.Sdk.Transports;

namespace Speckle.Connectors.DUI;

public static class ContainerRegistration
{
  public static void AddDUI<TThreadContext, TDocumentStore>(this IServiceCollection serviceCollection)
    where TDocumentStore : DocumentModelStore
    where TThreadContext : IThreadContext, new()
  {
    // context always newed up on host app's main/ui thread
    serviceCollection.AddSingleton<IThreadContext>(new TThreadContext());
    serviceCollection.AddSingleton<DocumentModelStore, TDocumentStore>();

    serviceCollection.AddTransient<IBrowserBridge, BrowserBridge>(); // POC: Each binding should have it's own bridge instance

    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)));
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IServerTransportFactory)));
    serviceCollection.AddEventsAsTransient(Assembly.GetAssembly(typeof(TDocumentStore)));
    serviceCollection.AddEventsAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)));
    serviceCollection.AddSingleton<IEventAggregator, EventAggregator>();

    serviceCollection.AddSingleton<IBinding, TopLevelExceptionHandlerBinding>(sp =>
      sp.GetRequiredService<TopLevelExceptionHandlerBinding>()
    );
    serviceCollection.AddSingleton<TopLevelExceptionHandlerBinding>();
    serviceCollection.AddSingleton<ITopLevelExceptionHandler, TopLevelExceptionHandler>();
    serviceCollection.AddTransient<ExceptionEvent>();
  }

  public static IServiceCollection AddEventsAsTransient(this IServiceCollection serviceCollection, Assembly assembly)
  {
    foreach (var type in assembly.ExportedTypes.Where(t => t.IsNonAbstractClass()))
    {
      if (type.FindInterfaces((i, _) => i == typeof(ISpeckleEvent), null).Length != 0)
      {
        serviceCollection.TryAddTransient(type);
      }
    }

    return serviceCollection;
  }

  public static IServiceProvider UseDUI(this IServiceProvider serviceProvider, bool isRevit = false)
  {
    //observe the unobserved!
    TaskScheduler.UnobservedTaskException += async (_, args) =>
    {
      await serviceProvider
        .GetRequiredService<IEventAggregator>()
        .GetEvent<ExceptionEvent>()
        .PublishAsync(args.Exception);
      serviceProvider.GetRequiredService<ILogger>().LogError(args.Exception, "Unobserved task exception");
      args.SetObserved();
    };

    if (!isRevit)
    {
      serviceProvider.GetRequiredService<DocumentModelStore>().OnDocumentStoreInitialized().Wait();
    }

    return serviceProvider;
  }
}
