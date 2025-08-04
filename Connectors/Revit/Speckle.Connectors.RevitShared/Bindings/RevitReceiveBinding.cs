using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.Bindings;

public sealed class RevitReceiveBinding(
  ICancellationManager cancellationManager,
  IBrowserBridge parent,
  ILogger<RevitReceiveBinding> logger,
  Speckle.Connectors.Revit.Operations.Receive.Settings.ToHostSettingsManager toHostSettingsManager,
  IRevitConversionSettingsFactory revitConversionSettingsFactory,
  IReceiveOperationManagerFactory receiveOperationManagerFactory
) : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; } = parent;
  private IReceiveBindingUICommands Commands { get; } = new ReceiveBindingUICommands(parent);

#pragma warning disable CA1024
  public List<ICardSetting> GetReceiveSettings() =>
    [new Speckle.Connectors.Revit.Operations.Receive.Settings.ReferencePointSetting(ReceiveReferencePointType.Source)];
#pragma warning restore CA1024

  public void CancelReceive(string modelCardId) => cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
          .Initialize(
            revitConversionSettingsFactory.Create(
              DetailLevelType.Coarse, // TODO figure out
              toHostSettingsManager.GetReferencePointSetting(card),
              false,
              true,
              false
            )
          );
      },
      async (_, processor) =>
      {
        try
        {
          return await processor();
        }
        catch (SpeckleRevitTaskException ex)
        {
          await SpeckleRevitTaskException.ProcessException(modelCardId, ex, logger, Commands);
          return null;
        }
      }
    );
  }
}
