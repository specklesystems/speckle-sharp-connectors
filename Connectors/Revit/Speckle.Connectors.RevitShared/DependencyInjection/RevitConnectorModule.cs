using System.Reflection;
using Autodesk.Revit.DB;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.Revit.Bindings;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Revit.Operations.Receive;
using Speckle.Connectors.Revit.Operations.Receive.Settings;
using Speckle.Connectors.Revit.Operations.Send;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.Common;
using Speckle.Sdk;
using Speckle.Sdk.Models.GraphTraversal;
#if !REVIT2026_OR_GREATER
using CefSharp;
#endif

namespace Speckle.Connectors.Revit.DependencyInjection;

// POC: should interface out things that are not
public static class ServiceRegistration
{
  public static void AddRevit(this IServiceCollection serviceCollection)
  {
    // Pre-load IronCompress's native Zstd lib (net48 only) at startup so BOTH the artefact send and receive paths can
    // (de)compress parquet — receive reads the bundle before the host builder runs. No-op on net8+ (Revit 2025+).
    ZstdNativeLoader.Ensure();

    serviceCollection.AddConnectors();
    serviceCollection.AddDUI<RevitThreadContext, RevitDocumentStore>();
    RegisterUiDependencies(serviceCollection);
    serviceCollection.AddMatchingInterfacesAsTransient(Assembly.GetExecutingAssembly());

    // Storage Schema
    serviceCollection.AddScoped<IdStorageSchema>();

    // POC: we need to review the scopes and create a document on what the policy is
    // and where the UoW should be
    // register UI bindings
    serviceCollection.AddSingleton<IBinding, TestBinding>();
    serviceCollection.AddSingleton<IBinding, ConfigBinding>();
    serviceCollection.AddSingleton<IBinding, AccountBinding>();
    serviceCollection.AddSingleton<IBinding, SelectionBinding>();
    serviceCollection.AddSingleton<IBinding, RevitSendBinding>();
    serviceCollection.AddSingleton<IBinding, RevitReceiveBinding>();
    serviceCollection.AddSingleton<RevitIdleManager>();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, BasicConnectorBindingRevit>();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IParametersBinding>());
    serviceCollection.AddSingleton<IParametersBinding, RevitParametersBinding>();

    // serviceCollection.AddSingleton<IAppIdleManager, RevitIdleManager>();

    // send operation and dependencies
    serviceCollection.AddScoped<SendOperation<DocumentToConvert>>();
    serviceCollection.AddScoped<ElementUnpacker>();
    serviceCollection.AddScoped<LevelUnpacker>();
    serviceCollection.AddScoped<ViewUnpacker>();
    serviceCollection.AddScoped<SendCollectionManager>();
    serviceCollection.AddScoped<IRootObjectBuilder<DocumentToConvert>, RevitRootObjectBuilder>();
    serviceCollection.AddScoped<IRootContinuousTraversalBuilder<DocumentToConvert>, RevitContinuousTraversalBuilder>();
    // Speckle 4.0 client-side artefact send (SGEO + eav + envelope parquet). Registering the builder makes
    // SendOperation route Revit sends through the artefact path.
    serviceCollection.AddScoped<IArtifactRootObjectBuilder<DocumentToConvert>, RevitArtifactRootObjectBuilder>();
    serviceCollection.AddSingleton<
      Speckle.Sdk.Pipelines.Send.Artifacts.IArtifactPipelineFactory,
      Speckle.Sdk.Pipelines.Send.Artifacts.ArtifactPipelineFactory
    >();
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();
    serviceCollection.AddSingleton<ToSpeckleSettingsManager>();
    serviceCollection.AddSingleton<ToHostSettingsManager>();
    serviceCollection.AddSingleton<LinkedModelHandler>();
    serviceCollection.AddSingleton<RoomsAndAreasHandler>();
    serviceCollection.AddSingleton<ParameterUpdater>();
    serviceCollection.AddSingleton<RevitParameterCreator>();
    serviceCollection.AddSingleton<RevitSendChangeTracker>();

    // receive operation and dependencies
    serviceCollection.AddScoped<IHostObjectBuilder, RevitHostObjectBuilder>();
    serviceCollection.AddScoped<ITransactionManager, TransactionManager>();

    // Speckle 4.0 artefact receive: download the parquet bundle + reconstruct the Base graph (DataObjects with
    // displayValue meshes → DirectShapes). PreferSolids = true: reconstruction only runs when receive-as-families is on
    // (RevitHostObjectArtefactBuilder.Build delegates to the v1 builder), and Revit DOES import 3dm — the rebuilt
    // RhinoObject.rawEncoding goes through IRawEncodedObjectConverter → DB.ShapeImporter → real solids [ENG-8800].
    serviceCollection.AddScoped<
      Speckle.Sdk.Pipelines.Receive.Artifacts.IArtifactDownloader,
      Speckle.Sdk.Pipelines.Receive.Artifacts.ArtifactDownloader
    >();
    serviceCollection.AddSingleton(new Speckle.Objects.Utils.ArtifactReceiveOptions(PreferSolids: true));
    serviceCollection.AddScoped<IArtifactReceiver, ArtifactReceiver>();
    // Native artefact receive (DirectShape from raw 3dm solids / SGEO meshes, Base-free). Registering this activates the direct-bake
    // branch in ReceiveOperation;
    serviceCollection.AddScoped<IArtifactHostObjectBuilder, RevitHostObjectArtefactBuilder>();
    serviceCollection.AddScoped<RevitArtefactSolidImporter>();
    serviceCollection.AddScoped<RevitFamilyBaker>();
    serviceCollection.AddScoped<FamilyGeometryBaker>();
    serviceCollection.AddScoped<RevitGroupBaker>();
    serviceCollection.AddScoped<RevitMaterialBaker>();
    serviceCollection.AddScoped<RevitViewBaker>();
    serviceCollection.AddScoped<RevitViewManager>();
    serviceCollection.AddScoped<DirectShapeUnpackStrategy>();
    serviceCollection.AddScoped<FamilyUnpackStrategy>();
    serviceCollection.AddScoped<RevitPreBakeSetupService>();
    serviceCollection.AddSingleton<RevitUtils>();
    serviceCollection.AddSingleton<FamilyCategoryUtils>();
    serviceCollection.AddSingleton<FamilyTransformUtils>();
    serviceCollection.AddSingleton<IFailuresPreprocessor, HideWarningsFailuresPreprocessor>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    serviceCollection.AddScoped<LocalToGlobalConverterUtils>();

    // operation progress manager
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();
  }

  public static void RegisterUiDependencies(IServiceCollection serviceCollection)
  {
#if !REVIT2026_OR_GREATER
    // different versions for different versions of CEF
    serviceCollection.AddSingleton(BindingOptions.DefaultBinder);
    serviceCollection.AddSingleton<CefSharpPanel>();
    serviceCollection.AddSingleton<IBrowserScriptExecutor>(c => c.GetRequiredService<CefSharpPanel>());
    serviceCollection.AddSingleton<IRevitPlugin, RevitCefPlugin>();
#else
    serviceCollection.AddSingleton<IRevitPlugin, RevitWebViewPlugin>();
    serviceCollection.AddSingleton<IBrowserScriptExecutor>(c => c.GetRequiredService<RevitControlWebView>());
    serviceCollection.AddSingleton<RevitControlWebView>();
    serviceCollection.AddSingleton<RevitControlWebViewDockable>();
#endif
  }
}
