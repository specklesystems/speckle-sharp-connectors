using Speckle.Autofac.DependencyInjection;
using Speckle.DllConflictManagement.Analytics;
using Speckle.DllConflictManagement.ConflictManagementOptions;
using Speckle.DllConflictManagement.EventEmitter;
using Speckle.DllConflictManagement.Serialization;

namespace Speckle.DllConflictManagement;

public static class ContainerRegistration
{
  public static void AddDllConflicts<T>(
    this SpeckleContainerBuilder builder,
    string hostApplication,
    string hostApplicationVersion
  )
    where T : class, IDllConflictUserNotifier
  {
    builder.AddTransient<IDllConflictUserNotifier, T>();
    builder.AddTransient<ISpeckleNewtonsoftSerializer, SpeckleNewtonsoftSerializer>();
    builder.AddTransient<IDllConflictEventEmitter, DllConflictEventEmitter>();
    builder.AddTransient<IDllConflictManager, DllConflictManager>();

    builder.AddTransient<IAnalyticsWithoutDependencies>(sp => new AnalyticsWithoutDependencies(
      sp.Resolve<IDllConflictEventEmitter>(),
      sp.Resolve<ISpeckleNewtonsoftSerializer>(),
      hostApplication,
      hostApplicationVersion
    ));
    builder.AddTransient<IDllConflictManagmentOptionsLoader>(sp => new DllConflictManagmentOptionsLoader(
      sp.Resolve<ISpeckleNewtonsoftSerializer>(),
      hostApplication,
      hostApplicationVersion
    ));
  }
}
