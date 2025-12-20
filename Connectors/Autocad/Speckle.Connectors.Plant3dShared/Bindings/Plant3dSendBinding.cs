using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Plant3dShared.Bindings;

public sealed class Plant3dSendBinding(
  DocumentModelStore store,
  IBrowserBridge parent,
  IEnumerable<ISendFilter> sendFilters,
  ICancellationManager cancellationManager,
  ISendConversionCache sendConversionCache,
  IAutocadConversionSettingsFactory autocadConversionSettingsFactory,
  IThreadContext threadContext,
  ITopLevelExceptionHandler topLevelExceptionHandler,
  IAppIdleManager appIdleManager,
  ISendOperationManagerFactory sendOperationManagerFactory
)
  : AutocadSendBaseBinding(
    store,
    parent,
    sendFilters,
    cancellationManager,
    sendConversionCache,
    threadContext,
    topLevelExceptionHandler,
    appIdleManager,
    sendOperationManagerFactory
  )
{
  protected override void InitializeSettings(IServiceProvider serviceProvider)
  {
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<AutocadConversionSettings>>()
      .Initialize(autocadConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));
  }
}

