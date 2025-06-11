using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3dShared;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Civil3dShared.Bindings;

public sealed class Civil3dReceiveBinding : AutocadReceiveBaseBinding
{
  private readonly ICivil3dConversionSettingsFactory _civil3dConversionSettingsFactory;
  private readonly IAutocadConversionSettingsFactory _autocadConversionSettingsFactory;

  public Civil3dReceiveBinding(
    IBrowserBridge parent,
    ICancellationManager cancellationManager,
    ICivil3dConversionSettingsFactory civil3dConversionSettingsFactory,
    IAutocadConversionSettingsFactory autocadConversionSettingsFactory,
    IThreadContext threadContext,
    IReceiveOperationManagerFactory receiveOperationManagerFactory
  )
    : base(parent, cancellationManager, threadContext, receiveOperationManagerFactory)
  {
    _civil3dConversionSettingsFactory = civil3dConversionSettingsFactory;
    _autocadConversionSettingsFactory = autocadConversionSettingsFactory;
  }

  // POC: we're registering the conversion settings for autocad here because we need the autocad conversion settings to be able to use the autocad typed converters.
  // POC: We need a separate receive binding for civil3d due to using a different unit converter (needed for conversion settings construction)
  protected override void InitializeSettings(IServiceProvider serviceProvider)
  {
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<Civil3dConversionSettings>>()
      .Initialize(_civil3dConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));

    serviceProvider
      .GetRequiredService<IConverterSettingsStore<AutocadConversionSettings>>()
      .Initialize(_autocadConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));
  }
}
