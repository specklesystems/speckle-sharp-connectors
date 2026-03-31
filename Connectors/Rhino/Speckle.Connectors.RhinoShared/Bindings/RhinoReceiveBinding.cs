using Microsoft.Extensions.DependencyInjection;
using Rhino;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Converters.Common;
using Speckle.Converters.Rhino;

namespace Speckle.Connectors.Rhino.Bindings;

public class RhinoReceiveBinding : IReceiveBinding
{
  private readonly ICancellationManager _cancellationManager;
  private readonly IRhinoConversionSettingsFactory _rhinoConversionSettingsFactory;
  private readonly IReceiveOperationManagerFactory _receiveOperationManagerFactory;
  private readonly ReceiveBindingUICommands _commands;

  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; }

  public RhinoReceiveBinding(
    ICancellationManager cancellationManager,
    IBrowserBridge parent,
    IRhinoConversionSettingsFactory rhinoConversionSettingsFactory,
    IReceiveOperationManagerFactory receiveOperationManagerFactory,
    DocumentModelStore store,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    _cancellationManager = cancellationManager;
    Parent = parent;
    _rhinoConversionSettingsFactory = rhinoConversionSettingsFactory;
    _receiveOperationManagerFactory = receiveOperationManagerFactory;

    _commands = new ReceiveBindingUICommands(parent);
    store.ReceiverSettingsChanged += (_, e) =>
      topLevelExceptionHandler.FireAndForget(async () =>
        await _commands.SetModelsExpired(new[] { e.ModelCardId }));
  }

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId)
  {
    // NOTE: introduction of AddVisualizationProperties setting not accounted for in receive pipeline, hence hardcoded as true (i.e. "as before")
    using var manager = _receiveOperationManagerFactory.Create();
    await manager.Process(
      _commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RhinoConversionSettings>>()
          .Initialize(_rhinoConversionSettingsFactory.Create(RhinoDoc.ActiveDoc, true));
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

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);
}
