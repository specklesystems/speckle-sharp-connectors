using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Converters.Autocad;
using Speckle.Converters.Plant3dShared;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Plant3dShared.Bindings;

public sealed class Plant3dReceiveBinding : AutocadReceiveBaseBinding
{
  private readonly IPlant3dConversionSettingsFactory _plant3dConversionSettingsFactory;
  private readonly IAutocadConversionSettingsFactory _autocadConversionSettingsFactory;

  public Plant3dReceiveBinding(
    IBrowserBridge parent,
    ICancellationManager cancellationManager,
    IPlant3dConversionSettingsFactory plant3dConversionSettingsFactory,
    IAutocadConversionSettingsFactory autocadConversionSettingsFactory,
    IThreadContext threadContext,
    IReceiveOperationManagerFactory receiveOperationManagerFactory
  )
    : base(parent, cancellationManager, threadContext, receiveOperationManagerFactory)
  {
    _plant3dConversionSettingsFactory = plant3dConversionSettingsFactory;
    _autocadConversionSettingsFactory = autocadConversionSettingsFactory;
  }

  // We need a separate receive binding for Plant3D due to using a different unit converter (needed for conversion settings construction)
  protected override void InitializeSettings(IServiceProvider serviceProvider, ModelCard mc)
  {
    serviceProvider
      .GetRequiredService<IConverterSettingsStore<Plant3dConversionSettings>>()
      .Initialize(_plant3dConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));

    serviceProvider
      .GetRequiredService<IConverterSettingsStore<AutocadConversionSettings>>()
      .Initialize(_autocadConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));
  }
}
