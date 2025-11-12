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
using Speckle.Converters.Common.ToHost;
using Speckle.Sdk;
using Speckle.Sdk.Models.GraphTraversal;
#if REVIT2026_OR_GREATER
using Speckle.Connectors.Revit2026.Plugin;
#else
using CefSharp;
#endif

namespace Speckle.Connectors.Revit.DependencyInjection;

// POC: should interface out things that are not
public static class ServiceRegistration
{
  public static void AddRevit(this IServiceCollection serviceCollection)
  {
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

    serviceCollection.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    serviceCollection.AddSingleton<IBasicConnectorBinding, BasicConnectorBindingRevit>();

    serviceCollection.AddSingleton<IAppIdleManager, RevitIdleManager>();

    // send operation and dependencies
    serviceCollection.AddScoped<SendOperation<DocumentToConvert>>();
    serviceCollection.AddScoped<ElementUnpacker>();
    serviceCollection.AddScoped<LevelUnpacker>();
    serviceCollection.AddScoped<SendCollectionManager>();
    serviceCollection.AddScoped<IRootObjectBuilder<DocumentToConvert>, RevitRootObjectBuilder>();
    serviceCollection.AddSingleton<ISendConversionCache, SendConversionCache>();
    serviceCollection.AddSingleton<ToSpeckleSettingsManager>();
    serviceCollection.AddSingleton<ToHostSettingsManager>();
    serviceCollection.AddSingleton<LinkedModelHandler>();

    // receive operation and dependencies
    serviceCollection.AddScoped<IHostObjectBuilder, RevitHostObjectBuilder>();
    serviceCollection.AddScoped<ITransactionManager, TransactionManager>();
    serviceCollection.AddScoped<RevitGroupBaker>();
    serviceCollection.AddScoped<RevitMaterialBaker>();
    serviceCollection.AddScoped<RevitViewManager>();
    serviceCollection.AddSingleton<RevitUtils>();
    serviceCollection.AddSingleton<IFailuresPreprocessor, HideWarningsFailuresPreprocessor>();
    serviceCollection.AddSingleton(DefaultTraversal.CreateTraversalFunc());
    serviceCollection.AddScoped<LocalToGlobalConverterUtils>();

    // register proxy display value manager
    serviceCollection.AddScoped<IProxyDisplayValueManager, ProxyDisplayValueManager>();

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
#elif !REVIT2026_OR_GREATER
    // different versions for different versions of CEF
    serviceCollection.AddSingleton(BindingOptions.DefaultBinder);

    var panel = new CefSharpPanel();
    panel.Browser.JavascriptObjectRepository.NameConverter = null;

    serviceCollection.AddSingleton(panel);
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
