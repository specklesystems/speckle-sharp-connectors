using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Operations.Send;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Credentials;
using Speckle.Sdk.Host;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.GrasshopperShared.Registration;

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
      services.AddRhinoConverters().AddConnectorUtils();

      // receive
      services.AddTransient<GrasshopperReceiveOperation>();
      services.AddSingleton(DefaultTraversal.CreateTraversalFunc());
      services.AddScoped<RootObjectUnpacker>();

      services.AddTransient<TraversalContextUnpacker>();
      services.AddTransient<AccountManager>();

      // send
      services.AddTransient<IRootObjectBuilder<SpeckleCollectionWrapperGoo>, GrasshopperRootObjectBuilder>();
      services.AddTransient<SendOperation<SpeckleCollectionWrapperGoo>>();
      services.AddSingleton<IThreadContext>(new DefaultThreadContext());

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
