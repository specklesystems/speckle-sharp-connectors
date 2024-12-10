using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Host;

namespace Speckle.Connectors.Grasshopper8;

public class PriorityLoader : GH_AssemblyPriority
{
  private IDisposable? _disposableLogger;
  public static ServiceProvider? Container { get; set; }

  public override GH_LoadingInstruction PriorityLoad()
  {
    try
    {
      var services = new ServiceCollection();
      _disposableLogger = services.Initialize(HostApplications.Grasshopper, GetVersion());

      services.AddRhinoConverters().AddGrasshopper().AddConnectorUtils();

      Container = services.BuildServiceProvider();
      return GH_LoadingInstruction.Proceed;
    }
    catch (Exception e) when (!e.IsFatal())
    {
      // TODO: Top level exception handling here
      return GH_LoadingInstruction.Abort;
    }
  }

  private HostAppVersion GetVersion()
  {
#if RHINO7
    return HostAppVersion.v7;
#elif RHINO8
    return HostAppVersion.v8;
#else
    throw new NotImplementedException();
#endif
  }
}
