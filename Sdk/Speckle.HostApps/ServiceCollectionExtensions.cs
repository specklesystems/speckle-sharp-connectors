using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Speckle.Connectors.Common.Extensions;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Testing;
using Speckle.HostApps.Framework;
using Speckle.Sdk;
using Speckle.Sdk.Api;

namespace Speckle.HostApps;

public static class ServiceCollectionExtensions
{
  public static void AddHostAppTesting<TTestBinding>(this IServiceCollection services)
    where TTestBinding : class, IBinding
  {
    services.AddSingleton<IBinding, TTestBinding>();
    services.AddMatchingInterfacesAsTransient(typeof(TestExecutor).Assembly);
  }

  public static void UseHostAppTesting(this IServiceCollection serviceCollection)
  {
    var testServices = new ServiceCollection();
    testServices.AddRange(serviceCollection);
    testServices.Replace(ServiceDescriptor.Singleton<DocumentModelStore, TestDocumentModelStore>());
    testServices.Replace(ServiceDescriptor.Singleton<IBrowserBridge, TestBrowserBridge>());
    testServices.Replace(ServiceDescriptor.Singleton<IOperations, TestOperations>());
    var serviceProvider = testServices.BuildServiceProvider();
    SpeckleXunitTestFramework.ServiceProvider =  serviceProvider;
  }
}
