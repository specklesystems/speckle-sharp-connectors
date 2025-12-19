using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Civil3dShared.Operations.Send.Settings;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Converters.Autocad;
using Speckle.Converters.Civil3dShared;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Civil3dShared.Bindings;

public sealed class Civil3dSendBinding : AutocadSendBaseBinding
{
  private readonly ICivil3dConversionSettingsFactory _civil3dConversionSettingsFactory;
  private readonly IAutocadConversionSettingsFactory _autocadConversionSettingsFactory;
  private readonly IToSpeckleSettingsManagerCivil3d _toSpeckleSettingsManagerCivil3d;
  private readonly DocumentModelStore _store;

  public Civil3dSendBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    IEnumerable<ISendFilter> sendFilters,
    ICancellationManager cancellationManager,
    ISendConversionCache sendConversionCache,
    ICivil3dConversionSettingsFactory civil3dConversionSettingsFactory,
    IAutocadConversionSettingsFactory autocadConversionSettingsFactory,
    IToSpeckleSettingsManagerCivil3d toSpeckleSettingsManagerCivil3d,
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
    _civil3dConversionSettingsFactory = civil3dConversionSettingsFactory;
    _autocadConversionSettingsFactory = autocadConversionSettingsFactory;
    _toSpeckleSettingsManagerCivil3d = toSpeckleSettingsManagerCivil3d;
    _store = store;
  }

  public override List<ICardSetting> GetSendSettings() => [new RevitCategoryMappingSetting(false)];

  // POC: we're registering the conversion settings for autocad here because we need the autocad conversion settings to be able to use the autocad typed converters.
  // POC: We need a separate send binding for civil3d due to using a different unit converter (needed for conversion settings construction)
  protected override void InitializeSettings(IServiceProvider serviceProvider)
  {
    // Get the model card from store to access user settings
    var modelCard = _store.GetSenders().FirstOrDefault();
    bool mappingToRevitCategories = false;
    if (modelCard != null)
    {
      mappingToRevitCategories = _toSpeckleSettingsManagerCivil3d.GetMappingToRevitCategories(modelCard);
    }

    serviceProvider
      .GetRequiredService<IConverterSettingsStore<Civil3dConversionSettings>>()
      .Initialize(
        _civil3dConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument, mappingToRevitCategories)
      );

    serviceProvider
      .GetRequiredService<IConverterSettingsStore<AutocadConversionSettings>>()
      .Initialize(_autocadConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));
  }
}
