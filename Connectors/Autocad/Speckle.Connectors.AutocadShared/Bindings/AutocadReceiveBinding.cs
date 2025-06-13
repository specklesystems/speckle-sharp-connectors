using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.Autocad;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Autocad.Bindings;

public sealed class AutocadReceiveBinding : AutocadReceiveBaseBinding
{
  private readonly IAutocadConversionSettingsFactory _autocadConversionSettingsFactory;

  public AutocadReceiveBinding(
    IBrowserBridge parent,
    ICancellationManager cancellationManager,
    IAutocadConversionSettingsFactory autocadConversionSettingsFactory,
    IThreadContext threadContext,
    IReceiveOperationManagerFactory receiveOperationManagerFactory,
    IAutocadDocumentActivationSuspension autocadDocumentActivationSuspension
  )
    : base(
      parent,
      cancellationManager,
      threadContext,
      receiveOperationManagerFactory,
      autocadDocumentActivationSuspension
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
