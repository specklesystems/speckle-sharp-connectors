using System.Reflection;
using Speckle.Autofac.DependencyInjection;

namespace Speckle.DllConflictManagement;

public static class DllConflicts
{
  public static bool Detect<T>(Assembly assembly, string hostApplication, string version)
    where T : class, IDllConflictUserNotifier
  {
    var conflictBuilder = SpeckleContainerBuilder.CreateInstance();
    conflictBuilder.AddDllConflicts<T>(hostApplication, version);
    var conflictContainer = conflictBuilder.Build();
    var manager = conflictContainer.Resolve<IDllConflictManager>();
    var conflictNotifier = conflictContainer.Resolve<IDllConflictUserNotifier>();
    try
    {
      manager.DetectConflictsWithAssembliesInCurrentDomain(assembly);
    }
    catch (TypeLoadException ex)
    {
      conflictNotifier.NotifyUserOfTypeLoadException(ex);
      return false;
    }
    catch (MemberAccessException ex)
    {
      conflictNotifier.NotifyUserOfMissingMethodException(ex);
      return false;
    }

    return true;
  }
}
