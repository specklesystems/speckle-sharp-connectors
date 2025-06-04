using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;

namespace Speckle.Connectors.Autocad.Bindings;

public abstract class AutocadReceiveBaseBinding(
  IBrowserBridge parent,
  ICancellationManager cancellationManager,
  IThreadContext threadContext, IReceiveOperationManagerFactory receiveOperationManagerFactory) : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; } = parent;


  private ReceiveBindingUICommands Commands { get; } = new (parent);

  protected abstract void InitializeSettings(IServiceProvider serviceProvider);

  public void CancelReceive(string modelCardId) => cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId) =>
    await threadContext.RunOnMainAsync(async () => await ReceiveInternal(modelCardId));

  private async Task ReceiveInternal(string modelCardId)
  {    
    using var manager = receiveOperationManagerFactory.Create();
    await manager.Process(Commands, modelCardId, InitializeSettings, async (_, processor) =>
    {
      try
      {
        // Disable document activation (document creation and document switch)
        // Not disabling results in DUI model card being out of sync with the active document
        // The DocumentActivated event isn't usable probably because it is pushed to back of main thread queue
        Application.DocumentManager.DocumentActivationEnabled = false;
        return await processor();
      }  
      
      finally
      {
        // reenable document activation
        Application.DocumentManager.DocumentActivationEnabled = true;

        // regenerate doc to flush graphics, sometimes some objects (ellipses, nurbs curves) do not appear fully visible after receive.
        // Adding a regen (must be run on main thread) here, but it doesn't seem to work:
        // it's run on main thread, tried sending the "regen" string to execute, also tried regen after every object bake, but still can't fix.
        // the objects should appear visible if you manually call the "regen" command after the operation finishes, or click on a view on the view cube which also calls regen.
        Application.DocumentManager.CurrentDocument.Editor.Regen();
      }
    });

  }
}
