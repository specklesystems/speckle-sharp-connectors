using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Autocad.Bindings;

public abstract class AutocadReceiveBaseBinding(
  IBrowserBridge parent,
  ICancellationManager cancellationManager,
  IThreadContext threadContext,
  IReceiveOperationManagerFactory receiveOperationManagerFactory,
  IAutocadDocumentActivationSuspension autocadDocumentActivationSuspension
) : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; } = parent;

  private ReceiveBindingUICommands Commands { get; } = new(parent);

  protected abstract void InitializeSettings(IServiceProvider serviceProvider);

  public void CancelReceive(string modelCardId) => cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId) =>
    await threadContext.RunOnMainAsync(async () => await ReceiveInternal(modelCardId));

  private async Task ReceiveInternal(string modelCardId)
  {
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(
      Commands,
      modelCardId,
      InitializeSettings,
      async (_, processor) =>
      {
        try
        {
          using var __ = autocadDocumentActivationSuspension.Suspend();
          return await processor();
        }
        finally
        {
          // regenerate doc to flush graphics, sometimes some objects (ellipses, nurbs curves) do not appear fully visible after receive.
          // Adding a regen (must be run on main thread) here, but it doesn't seem to work:
          // it's run on main thread, tried sending the "regen" string to execute, also tried regen after every object bake, but still can't fix.
          // the objects should appear visible if you manually call the "regen" command after the operation finishes, or click on a view on the view cube which also calls regen.
          Application.DocumentManager.CurrentDocument.Editor.Regen();
        }
      }
    );
  }
}
