using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;
#if RHINO_MAC
using Speckle.Connectors.Rhino.HostApp;
#endif

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
    // NOTE: introduction of AddVisualizationProperties setting not accounted for in receive pipeline, hence hardcoded as true (i.e. "as before")
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
          .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc, true));
      },
      async (modelName, processor) =>
      {
        uint undoRecord = 0;
        try
        {
#if RHINO_MAC
          // Like the other document writes, undo records are opened and closed on Rhino's main thread on macOS. FEA-124.
          MacMainThread.Run(() =>
            undoRecord = RhinoDoc.ActiveDoc.BeginUndoRecord($"Receive Speckle model {modelName}")
          );
#else
          undoRecord = RhinoDoc.ActiveDoc.BeginUndoRecord($"Receive Speckle model {modelName}");
#endif
          return await processor();
        }
        finally
        {
#if RHINO_MAC
          MacMainThread.Run(() => RhinoDoc.ActiveDoc.EndUndoRecord(undoRecord));
#else
          RhinoDoc.ActiveDoc.EndUndoRecord(undoRecord);
#endif
        }
      }
    );
  }

  public void CancelSend(string modelCardId) => cancellationManager.CancelOperation(modelCardId);
}
