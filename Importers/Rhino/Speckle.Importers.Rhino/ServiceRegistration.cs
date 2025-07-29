using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Formatting.Compact;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;

namespace Speckle.Importers.Rhino;

public static class ServiceRegistration
{
  public static IServiceCollection AddRhinoImporter(this IServiceCollection services)
  {
    services.Initialize(HostApplications.RhinoImporter, HostAppVersion.v8);
    services.AddRhino(false);
    services.AddRhinoConverters();
    services.AddLoggingConfig();
    services.AddTransient<Importer>();
    services.AddTransient<Progress>();

    // override default thread context
    services.AddSingleton<IThreadContext>(new ImporterThreadContext());

    return services;
  }

  private static IServiceCollection AddLoggingConfig(this IServiceCollection services)
  {
    Log.Logger = new LoggerConfiguration()
      .Enrich.FromLogContext()
      .WriteTo.Console(new RenderedCompactJsonFormatter())
      .CreateLogger();
    services.AddLogging(loggingBuilder =>
    {
      loggingBuilder.ClearProviders();
      loggingBuilder.AddSerilog(dispose: true);
    });
    return services;
  }
}
