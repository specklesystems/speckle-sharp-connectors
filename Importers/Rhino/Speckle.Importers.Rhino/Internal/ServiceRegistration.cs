﻿using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.Rhino.DependencyInjection;
using Speckle.Converters.Rhino;

namespace Speckle.Importers.Rhino.Internal;

internal static class ServiceRegistration
{
  public static IServiceCollection AddRhinoImporter(this IServiceCollection services)
  {

    services.Initialize(HostApplications.RhinoImporter, HostAppVersion.v8);

    services.AddRhino(false);
    services.AddRhinoConverters();
    services.AddTransient<Progress>();
    services.AddTransient<Sender>();
    services.AddTransient<ImporterInstance>();

    // override default thread context
    services.AddSingleton<IThreadContext>(new ImporterThreadContext());

    return services;
  }
}
