using Grasshopper;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Operations.Send;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.GrasshopperShared.Registration;

public class PriorityLoader : GH_AssemblyPriority
{
  private IDisposable? _disposableLogger;
  public static ServiceProvider? Container { get; set; }

  public override GH_LoadingInstruction PriorityLoad()
  {
    Instances.ComponentServer.AddCategoryIcon(ComponentCategories.PRIMARY_RIBBON, Resources.speckle_logo);
    Instances.ComponentServer.AddCategorySymbolName(ComponentCategories.PRIMARY_RIBBON, 'S');

    try
    {
      var services = new ServiceCollection();
      _disposableLogger = services.Initialize(HostApplications.Grasshopper, GetVersion());
      services.AddRhinoConverters();
      services.AddConnectors();

      // receive
      services.AddTransient<GrasshopperReceiveOperation>();
      services.AddSingleton(DefaultTraversal.CreateTraversalFunc());
      services.AddTransient<TraversalContextUnpacker>();

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
