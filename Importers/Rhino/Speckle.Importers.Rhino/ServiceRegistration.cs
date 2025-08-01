using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;
using Speckle.Importers.Rhino.Internal;
using Speckle.Sdk;
using Point = Speckle.Objects.Geometry.Point;

namespace Speckle.Importers.Rhino;

public static class ServiceRegistration
{
  public static IServiceCollection AddRhinoImporter(this IServiceCollection services)
  {
    services.AddSpeckleSdk(
      HostApplications.RhinoImporter,
      HostApplications.GetVersion(HostAppVersion.v8),
      Assembly.GetExecutingAssembly().GetVersion(),
      typeof(Point).Assembly
    );
    // services.Initialize(HostApplications.RhinoImporter, HostAppVersion.v8);
    services.AddRhino(false);
    services.AddRhinoConverters();
    services.AddTransient<Progress>();
    services.AddTransient<Sender>();

    // override default thread context
    services.AddSingleton<IThreadContext>(new ImporterThreadContext());

    return services;
  }
}
