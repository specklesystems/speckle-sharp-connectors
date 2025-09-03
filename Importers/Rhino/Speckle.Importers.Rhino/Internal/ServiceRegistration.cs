using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.SQLite;

namespace Speckle.Importers.Rhino.Internal;

internal static class ServiceRegistration
{
  public static IServiceCollection AddRhinoImporter(this IServiceCollection services, Application applicationInfo)
  {
    services.Initialize(applicationInfo, HostAppVersion.v8);
    services.AddSingleton(applicationInfo);

    services.AddRhino(false);
    services.AddRhinoConverters();
    services.AddTransient<Progress>();
    services.AddTransient<Sender>();
    services.AddTransient<ImporterInstance>();

    // override default thread context
    services.AddSingleton<IThreadContext>(new ImporterThreadContext());

    // override sqlite cache, since we don't want to persist to disk any object data
    services.AddTransient<ISqLiteJsonCacheManagerFactory, DummySqliteJsonCacheManagerFactory>();

    return services;
  }
}
