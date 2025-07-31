using Grasshopper;
using Grasshopper.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Instances;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.GrasshopperShared.Components;
using Speckle.Connectors.GrasshopperShared.HostApp;
using Speckle.Connectors.GrasshopperShared.Operations.Receive;
using Speckle.Connectors.GrasshopperShared.Operations.Send;
using Speckle.Connectors.GrasshopperShared.Parameters;
using Speckle.Connectors.GrasshopperShared.Properties;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.GrasshopperShared.Registration;

public class PriorityLoader : GH_AssemblyPriority
{
  private IDisposable? _disposableLogger;
  public static ServiceProvider? Container { get; set; }

  public static IServiceScope CreateScopeForActiveDocument()
  {
    var scope = Container.CreateScope();
    var rhinoConversionSettingsFactory = scope.ServiceProvider.GetRequiredService<IRhinoConversionSettingsFactory>();
    scope
      .ServiceProvider.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
      .Initialize(rhinoConversionSettingsFactory.Create(CurrentDocument.Document.NotNull()));
    return scope;
  }
  
  private void OnDocumentAdded(GH_DocumentServer sender, GH_Document doc)
  {
    // Add events for solution start and end
    doc.SolutionStart += DocumentOnSolutionStart;
    doc.SolutionEnd += DocumentOnSolutionEnd;
  }

  private void OnDocumentRemoved(GH_DocumentServer sender, GH_Document doc)
  {
    // Remove events for solution start and end
    doc.SolutionStart -= DocumentOnSolutionStart;
    doc.SolutionEnd -= DocumentOnSolutionEnd;
  }

  private void DocumentOnSolutionStart(object sender, GH_SolutionEventArgs e) => CurrentDocument.SetupHeadlessDoc();

  private void DocumentOnSolutionEnd(object sender, GH_SolutionEventArgs e) => CurrentDocument.DisposeHeadlessDoc();

  public override GH_LoadingInstruction PriorityLoad()
  {
#if RHINO7_OR_GREATER
    if (Instances.RunningHeadless)
    {
      // If GH is running headless, we listen for document added/removed events.
      Instances.DocumentServer.DocumentAdded += OnDocumentAdded;
      Instances.DocumentServer.DocumentRemoved += OnDocumentRemoved;
    }
#endif
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
      services.AddScoped<
        IInstanceObjectsManager<SpeckleGeometryWrapper, List<string>>,
        InstanceObjectsManager<SpeckleGeometryWrapper, List<string>>
      >(); // each send operation gets its own InstanceObjectsManager instance (scoped = per-operation)

      services.AddScoped<SpeckleConversionContext>();
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
