using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;

namespace Speckle.Connectors.Rhino.Bindings;

public class RhinoReceiveBinding(
  ICancellationManager cancellationManager,
  IBrowserBridge parent,
  IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
  IReceiveOperationManagerFactory receiveOperationManagerFactory
) : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; } = parent;

  private ReceiveBindingUICommands Commands { get; } = new(parent);

  public void CancelReceive(string modelCardId) => cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
          .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc));
      },
      async (modelName, processor) =>
      {
        uint undoRecord = 0;
        try
        {
          undoRecord = RhinoDoc.ActiveDoc.BeginUndoRecord($"Receive Speckle model {modelName}");
          return await processor();
        }
        finally
        {
          RhinoDoc.ActiveDoc.EndUndoRecord(undoRecord);
        }
      }
    );
  }

  public void CancelSend(string modelCardId) => cancellationManager.CancelOperation(modelCardId);
}
