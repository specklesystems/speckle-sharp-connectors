using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Autocad.Bindings;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Converters.Civil3d;
using Speckle.Converters.Common;
using Speckle.Sdk;

namespace Speckle.Connectors.Civil3d2024;

public sealed class Civil3dSendBinding : AutocadSendBaseBinding
{
  private readonly ICivil3dConversionSettingsFactory _civil3dConversionSettingsFactory;

  public Civil3dSendBinding(DocumentModelStore store, IAppIdleManager idleManager, IBrowserBridge parent, IEnumerable<ISendFilter> sendFilters, CancellationManager cancellationManager, IServiceProvider serviceProvider,
    ISendConversionCache sendConversionCache, IOperationProgressManager operationProgressManager, ILogger<AutocadSendBinding> logger, ICivil3dConversionSettingsFactory civil3dConversionSettingsFactory, 
    ISpeckleApplication speckleApplication) : base(store, idleManager, parent, sendFilters, cancellationManager, serviceProvider, sendConversionCache, operationProgressManager, logger, speckleApplication)
  {
    _civil3dConversionSettingsFactory = civil3dConversionSettingsFactory;
  }

  protected override void InitializeSettings(IServiceProvider serviceProvider)
  {
    serviceProvider.GetRequiredService<IConverterSettingsStore<Civil3dConversionSettings>>()
      .Initialize(_civil3dConversionSettingsFactory.Create(Application.DocumentManager.CurrentDocument));
  }
}
