using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Sdk;
using Speckle.Sdk.Common;
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

    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IdleCallManager)).NotNull());
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetAssembly(typeof(IServerTransportFactory)).NotNull());

    serviceCollection.AddSingleton<IBinding, TopLevelExceptionHandlerBinding>(sp =>
      sp.GetRequiredService<TopLevelExceptionHandlerBinding>()
    );
    serviceCollection.AddSingleton<TopLevelExceptionHandlerBinding>();
    serviceCollection.AddSingleton<ITopLevelExceptionHandler, TopLevelExceptionHandler>();
  }

  public static void UseDUI(this IServiceProvider serviceProvider)
  {
    //observe the unobserved!
    TaskScheduler.UnobservedTaskException += (_, args) =>
    {
      try
      {
        var stackTrace = args.Exception.StackTrace;
        if (stackTrace?.IndexOf("Speckle", StringComparison.InvariantCultureIgnoreCase) <= 0)
        {
          serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnobservedTaskException")
            .LogInformation(args.Exception, "Non-Speckle unobserved task exception");
        }
        else
        {
          serviceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("UnobservedTaskException")
            .LogError(args.Exception, "Unobserved task exception");
          args.SetObserved();
        }
      }
#pragma warning disable CA1031
      catch (Exception e)
#pragma warning restore CA1031
      {
        Console.WriteLine("Error logging unobserved task exception");
        Console.WriteLine(args.Exception);
        Console.WriteLine(e);
      }
    };
  }
}
