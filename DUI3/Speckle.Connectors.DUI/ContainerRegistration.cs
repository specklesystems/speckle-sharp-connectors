using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Sdk;

namespace Speckle.Connectors.DUI;

public static class ContainerRegistration
{
  public static void AddDUI<TDocumentStore, THostObjectBuilder, TThreadContext>(
    this IServiceCollection serviceCollection,
    Application application,
    HostAppVersion applicationVersion,
    string? speckleVersion = null,
    IEnumerable<Assembly>? assemblies = null
  )
    where TDocumentStore : DocumentModelStore
    where THostObjectBuilder : class, IHostObjectBuilder
    where TThreadContext : IThreadContext, new()
  {
    serviceCollection.AddSpeckleLogging(application, applicationVersion);
    serviceCollection.AddConnectors<THostObjectBuilder, TThreadContext>(
      application,
      applicationVersion,
      speckleVersion,
      assemblies
    );
    serviceCollection.AddSingleton<DocumentModelStore, TDocumentStore>();
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());
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
        serviceProvider
          .GetRequiredService<ILoggerFactory>()
          .CreateLogger("UnobservedTaskException")
          .LogError(args.Exception, "Unobserved task exception");
      }
#pragma warning disable CA1031
      catch (Exception e)
#pragma warning restore CA1031
      {
        Console.WriteLine("Error logging unobserved task exception");
        Console.WriteLine(args.Exception);
        Console.WriteLine(e);
      }
      finally
      {
        args.SetObserved();
      }
    };
  }
}
