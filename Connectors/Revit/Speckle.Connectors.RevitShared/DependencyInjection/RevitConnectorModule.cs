using Autodesk.Revit.DB;
using CefSharp;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.Common.Builders;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.Revit.Bindings;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Revit.Operations.Receive;
using Speckle.Connectors.Revit.Operations.Send;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.Utils;
using Speckle.Connectors.Utils.Builders;
using Speckle.Connectors.Utils.Caching;
using Speckle.Connectors.Utils.Operations;
using Speckle.Converters.Common;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Revit.DependencyInjection;

// POC: should interface out things that are not
public static class ServiceRegistration
{
  public static void AddRevit(this IServiceCollection serviceCollection)
  {
    serviceCollection.AddConnectorUtils();
    serviceCollection.AddDUI();
    RegisterUiDependencies(serviceCollection);

    // register
    serviceCollection.AddSingleton<DocumentModelStore, RevitDocumentStore>();

    // Storage Schema
    serviceCollection.AddScoped<DocumentModelStorageSchema>();
    serviceCollection.AddScoped<IdStorageSchema>();

    // POC: we need to review the scopes and create a document on what the policy is
    // and where the UoW should be
    // register UI bindings
    builder.AddSingleton<IBinding, TestBinding>();
    builder.AddSingleton<IBinding, ConfigBinding>("connectorName", "Revit"); // POC: Easier like this for now, should be cleaned up later
    builder.AddSingleton<IBinding, AccountBinding>();
    builder.AddSingleton<IBinding, SelectionBinding>();
    builder.AddSingleton<IBinding, RevitSendBinding>();
    builder.AddSingleton<IBinding, RevitReceiveBinding>();
    builder.AddSingleton<IRevitIdleManager, RevitIdleManager>();

    serviceCollection.RegisterTopLevelExceptionHandler();

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, BasicConnectorBindingRevit>();

    // send operation and dependencies
    serviceCollection.AddScoped<SendOperation<ElementId>>();
    serviceCollection.AddScoped<ElementUnpacker>();
    serviceCollection.AddScoped<SendCollectionManager>();
    serviceCollection.AddScoped<IRootObjectBuilder<ElementId>, RevitRootObjectBuilder>();
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();
    serviceCollection.AddSingleton<ToSpeckleSettingsManager>();

    // receive operation and dependencies
    builder.AddScoped<IHostObjectBuilder, RevitHostObjectBuilder>();
    builder.AddScoped<ITransactionManager, TransactionManager>();
    builder.AddScoped<RevitGroupBaker>();
    builder.AddScoped<RevitMaterialBaker>();
    builder.AddSingleton<RevitUtils>();
    builder.AddSingleton<IFailuresPreprocessor, HideWarningsFailuresPreprocessor>();
    builder.AddSingleton(DefaultTraversal.CreateTraversalFunc());

    builder.AddScoped<LocalToGlobalConverterUtils>();

    // operation progress manager
    serviceCollection.AddSingleton<IOperationProgressManager, OperationProgressManager>();
  }

  public static void RegisterUiDependencies(IServiceCollection serviceCollection)
  {
#if REVIT2022
    //different versons for different versions of CEF
    serviceCollection.AddSingleton(new BindingOptions() { CamelCaseJavascriptNames = false });
    serviceCollection.AddSingleton<CefSharpPanel>();
    serviceCollection.AddSingleton<IBrowserScriptExecutor>(sp => sp.GetRequiredService<CefSharpPanel>());
    serviceCollection.AddSingleton<IRevitPlugin, RevitCefPlugin>();
#else
    // different versions for different versions of CEF
    builder.AddSingleton(BindingOptions.DefaultBinder);

    var panel = new CefSharpPanel();
    panel.Browser.JavascriptObjectRepository.NameConverter = null;

    serviceCollection.AddSingleton(panel);
    serviceCollection.AddSingleton<IBrowserScriptExecutor>(c => c.GetRequiredService<CefSharpPanel>());
    serviceCollection.AddSingleton<IRevitPlugin, RevitCefPlugin>();
#endif
  }
}
