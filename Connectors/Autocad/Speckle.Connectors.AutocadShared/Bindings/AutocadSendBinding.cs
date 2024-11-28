using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.HostApp;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Connectors.Autocad.Bindings;

public sealed class AutocadSendBinding : AutocadSendBaseBinding
{
  private readonly IAutocadConversionSettingsFactory _autocadConversionSettingsFactory;

  public AutocadSendBinding(
    DocumentModelStore store,
    IAutocadIdleManager idleManager,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    CancellationManager cancellationManager,
    IServiceProvider serviceProvider,
    ISendConversionCache sendConversionCache,
    IOperationProgressManager operationProgressManager,
    ILogger<AutocadSendBinding> logger,
    IAutocadConversionSettingsFactory autocadConversionSettingsFactory,
    ISpeckleApplication speckleApplication,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IThreadContext threadContext
  )
    : base(
      store,
      idleManager,
      parent,
      sendFilters,
      cancellationManager,
      serviceProvider,
      sendConversionCache,
      operationProgressManager,
      logger,
      speckleApplication,
      topLevelExceptionHandler,
      threadContext
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
