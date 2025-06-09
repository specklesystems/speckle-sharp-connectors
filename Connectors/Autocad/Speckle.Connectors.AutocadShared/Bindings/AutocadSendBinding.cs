using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Autocad.Bindings;

public sealed class AutocadSendBinding : AutocadSendBaseBinding
{
  private readonly IAutocadConversionSettingsFactory _autocadConversionSettingsFactory;

  public AutocadSendBinding(
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
    : base(
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
    _autocadConversionSettingsFactory = autocadConversionSettingsFactory;
  }

  protected override void InitializeSettings(IServiceProvider serviceProvider)
  {
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<AutocadConversionSettings>>()
      .Initialize(_autocadConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));
  }
}
