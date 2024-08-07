using System.IO;
using System.Reflection;
using Autodesk.Revit.UI;
using Speckle.Autofac;
using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils;
using Speckle.Core.Common;
using Speckle.Core.Kits;
using Speckle.Core.Logging;

namespace Speckle.Connectors.Revit.Plugin;

internal sealed class RevitExternalApplication : IExternalApplication
{
  private IRevitPlugin? _revitPlugin;

  private SpeckleContainer? _container;
  private IDisposable? _disposableLogger;

  // POC: this is getting hard coded - need a way of injecting it
  //      I am beginning to think the shared project is not the way
  //      and an assembly which is invoked with some specialisation is the right way to go
  //      maybe subclassing, or some hook to inject som configuration
  private readonly RevitSettings _revitSettings;

  // POC: move to somewhere central?
  public static readonly DockablePaneId DockablePanelId = new(new Guid("{f7b5da7c-366c-4b13-8455-b56f433f461e}"));

  public RevitExternalApplication()
  {
    // POC: load from JSON file?
    _revitSettings = new RevitSettings(
      "Speckle New UI",
      "Speckle",
      "Speckle New UI",
      GetVersionAsString(),
      "Speckle New UI",
      "Revit",
      [Path.GetDirectoryName(typeof(RevitExternalApplication).Assembly.Location).NotNull()],
      HostApplications.Revit.Slug,
      GetVersionAsString() //POC: app version?
    );
  }

  private string GetVersionAsString() => HostApplications.GetVersion(GetVersion());

  private HostAppVersion GetVersion()
  {
#if REVIT2022
    return HostAppVersion.v2022;
#elif REVIT2023
    return HostAppVersion.v2023;
#elif REVIT2024
    return HostAppVersion.v2024;
#elif REVIT2025
    return HostAppVersion.v2025;
#else
    throw new NotImplementedException();
#endif
  }

  public Result OnStartup(UIControlledApplication application)
  {
    try
    {
      // POC: not sure what this is doing...  could be messing up our Aliasing????
      AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.OnAssemblyResolve<RevitExternalApplication>;
      // init DI
      _disposableLogger = Setup.Initialize(Config.Create(HostApplications.Revit, GetVersion()));
      _container = SpeckleContainerBuilder
        .CreateInstance()
        .LoadAutofacModules(Assembly.GetExecutingAssembly(), _revitSettings.ModuleFolders.NotNull())
        .AddSingleton(_revitSettings) // apply revit settings into DI
        .AddSingleton(application) // inject UIControlledApplication application
        .Build();

      // resolve root object
      _revitPlugin = _container.Resolve<IRevitPlugin>();
      _revitPlugin.Initialise();
    }
    catch (Exception e) when (!e.IsFatal())
    {
      // POC: feedback?
      return Result.Failed;
    }

    return Result.Succeeded;
  }

  public Result OnShutdown(UIControlledApplication application)
  {
    try
    {
      // POC: could this be more a generic Connector Init() Shutdown()
      // possibly with injected pieces or with some abstract methods?
      // need to look for commonality
      _revitPlugin?.Shutdown();
      _disposableLogger?.Dispose();
    }
    catch (Exception e) when (!e.IsFatal())
    {
      // POC: feedback?
      return Result.Failed;
    }

    return Result.Succeeded;
  }
}
