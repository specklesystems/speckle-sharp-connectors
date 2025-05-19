using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Operations;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Logging;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;

namespace Speckle.Connectors.Autocad.Bindings;

public abstract class AutocadReceiveBaseBinding : IReceiveBinding
{
  public string Name => "receiveBinding";
  public IBrowserBridge Parent { get; }

  private readonly DocumentModelStore _store;
  private readonly ICancellationManager _cancellationManager;
  private readonly IServiceProvider _serviceProvider;
  private readonly IOperationProgressManager _operationProgressManager;
  private readonly ILogger<AutocadReceiveBinding> _logger;
  private readonly ISpeckleApplication _speckleApplication;
  private readonly IThreadContext _threadContext;
  private readonly IAutocadDocumentActivationSuspension _documentActivationSuspension;

  private ReceiveBindingUICommands Commands { get; }

  protected AutocadReceiveBaseBinding(
    DocumentModelStore store,
    IBrowserBridge parent,
    ICancellationManager cancellationManager,
    IServiceProvider serviceProvider,
    IOperationProgressManager operationProgressManager,
    ILogger<AutocadReceiveBinding> logger,
    ISpeckleApplication speckleApplication,
    IThreadContext threadContext,
    IAutocadDocumentActivationSuspension documentActivationSuspension
  )
  {
    _store = store;
    _cancellationManager = cancellationManager;
    _serviceProvider = serviceProvider;
    _operationProgressManager = operationProgressManager;
    _logger = logger;
    _speckleApplication = speckleApplication;
    _threadContext = threadContext;
    Parent = parent;
    Commands = new ReceiveBindingUICommands(parent);
    _documentActivationSuspension = documentActivationSuspension;
  }

  protected abstract void InitializeSettings(IServiceProvider serviceProvider);

  public void CancelReceive(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public async Task Receive(string modelCardId) =>
    await _threadContext.RunOnMainAsync(async () => await ReceiveInternal(modelCardId));

  private async Task ReceiveInternal(string modelCardId)
  {
    using var scope = _serviceProvider.CreateScope();
    InitializeSettings(scope.ServiceProvider);

    try
    {
      // Get receiver card
      if (_store.GetModelById(modelCardId) is not ReceiverModelCard modelCard)
      {
        // Handle as GLOBAL ERROR at BrowserBridge
        throw new InvalidOperationException("No download model card was found.");
      }

      using var cancellationItem = _cancellationManager.GetCancellationItem(modelCardId);

      // Disable document activation (document creation and document switch)
      // Not disabling results in DUI model card being out of sync with the active document
      // The DocumentActivated event isn't usable probably because it is pushed to back of main thread queue
      using var _ = _documentActivationSuspension.Suspend();

      // Receive host objects
      var operationResults = await scope
        .ServiceProvider.GetRequiredService<ReceiveOperation>()
        .Execute(
          modelCard.GetReceiveInfo(_speckleApplication.Slug),
          _operationProgressManager.CreateOperationProgressEventHandler(Parent, modelCardId, cancellationItem.Token),
          cancellationItem.Token
        );

      await Commands.SetModelReceiveResult(
        modelCardId,
        operationResults.BakedObjectIds,
        operationResults.ConversionResults
      );
    }
    catch (OperationCanceledException)
    {
      // SWALLOW -> UI handles it immediately, so we do not need to handle anything for now!
      // Idea for later -> when cancel called, create promise from UI to solve it later with this catch block.
      // So have 3 state on UI -> Cancellation clicked -> Cancelling -> Cancelled
      return;
    }
    catch (Exception ex) when (!ex.IsFatal()) // UX reasons - we will report operation exceptions as model card error. We may change this later when we have more exception documentation
    {
      _logger.LogModelCardHandledError(ex);
      await Commands.SetModelError(modelCardId, ex);
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
}
