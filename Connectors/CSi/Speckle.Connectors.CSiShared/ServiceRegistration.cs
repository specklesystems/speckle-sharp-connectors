﻿using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common;
using Speckle.Connectors.CSiShared.Bindings;
using Speckle.Connectors.CSiShared.Filters;
using Speckle.Connectors.CSiShared.HostApp;
using Speckle.Connectors.DUI;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.WebView;

namespace Speckle.Connectors.CSiShared;

public static class ServiceRegistration
{
  // TODO: AddCSi and AddETABS for shared and specific implementations respectively. To do with CNX-828
  public static IServiceCollection AddETABS(this IServiceCollection services)
  {
    services.AddSingleton<IBrowserBridge, BrowserBridge>();
    services.AddSingleton<ICSiApplicationService, CSiApplicationService>();

    services.AddConnectorUtils();
    services.AddDUI<CSiSharedDocumentModelStore>();
    services.AddDUIView();

    services.AddSingleton<DocumentModelStore, CSiSharedDocumentModelStore>();

    services.AddSingleton<IBinding, TestBinding>();
    services.AddSingleton<IBinding, ConfigBinding>();
    services.AddSingleton<IBinding, AccountBinding>();

    services.AddSingleton<IBinding>(sp => sp.GetRequiredService<IBasicConnectorBinding>());
    services.AddSingleton<IBasicConnectorBinding, CSiSharedBasicConnectorBinding>();
    services.AddSingleton<IAppIdleManager, CSiSharedIdleManager>();

    services.AddSingleton<IBinding, CSiSharedSelectionBinding>();
    services.AddSingleton<IBinding, CSiSharedSendBinding>();

    services.AddScoped<ISendFilter, CSiSharedSelectionFilter>();

    services.RegisterTopLevelExceptionHandler();

    return services;
  }
}
