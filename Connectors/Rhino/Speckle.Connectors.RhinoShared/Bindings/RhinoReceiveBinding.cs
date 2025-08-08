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
    // NOTE: introduction of sendVertexNormals and sendTextureCoordinates in Rhino not accounted for in receive pipeline, hence hardcoded as true (i.e. "as before")
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
          .Initialize(rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc, true, true));
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
