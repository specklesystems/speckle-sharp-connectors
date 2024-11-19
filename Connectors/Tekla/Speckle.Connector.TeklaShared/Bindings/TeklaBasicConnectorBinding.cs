using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;
using Tekla.Structures;

namespace Speckle.Connector.Tekla2024.Bindings;

public class TeklaBasicConnectorBinding : IBasicConnectorBinding
{
  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }
  private readonly ILogger<TeklaBasicConnectorBinding> _logger;
  private readonly TSM.Model _model;

  public TeklaBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store,
    ILogger<TeklaBasicConnectorBinding> logger,
    TSM.Model model
  )
  {
    _speckleApplication = speckleApplication;
    _store = store;
    Parent = parent;
    _logger = logger;
    _model = model;
  }

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public DocumentInfo? GetDocumentInfo() => new DocumentInfo("Test", "Test", "Test");

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.Models.Add(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public async Task HighlightModel(string modelCardId)
  {
    try
    {
      var model = _store.GetModelById(modelCardId);
      if (model == null)
      {
        _logger.LogError("Model was null when highlighting received model");
        return;
      }

      List<string> objectIds = new();
      if (model is SenderModelCard senderModel)
      {
        objectIds = senderModel.SendFilter?.RefreshObjectIds() ?? new List<string>();
      }
      else if (model is ReceiverModelCard receiverModel)
      {
        objectIds = receiverModel.BakedObjectIds?.ToList() ?? new List<string>();
      }

      if (objectIds.Count == 0)
      {
        await Commands
          .SetModelError(modelCardId, new OperationCanceledException("No objects found to highlight."))
          .ConfigureAwait(false);
        return;
      }

      await HighlightObjects(objectIds).ConfigureAwait(false);
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "Failed to highlight model");
      await Commands.SetModelError(modelCardId, ex).ConfigureAwait(false);
    }
  }

  public async Task HighlightObjects(IReadOnlyList<string> objectIds)
  {
    try
    {
      await Task.Run(() =>
        {
          var modelObjects = objectIds
            .Select(id => _model.SelectModelObject(new Identifier(new Guid(id))))
            .Where(obj => obj != null)
            .ToList();

          TSM.Operations.Operation.Highlight(modelObjects);
        })
        .ConfigureAwait(false);
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "Failed to highlight objects");
    }
  }

  public BasicConnectorBindingCommands Commands { get; }
}
