using System.Collections;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Sdk;
using Tekla.Structures;
using Tekla.Structures.Geometry3d;

namespace Speckle.Connectors.TeklaShared.Bindings;

public class TeklaBasicConnectorBinding : IBasicConnectorBinding
{
  public BasicConnectorBindingCommands Commands { get; }
  private readonly ISpeckleApplication _speckleApplication;
  private readonly DocumentModelStore _store;
  public string Name => "baseBinding";
  public IBrowserBridge Parent { get; }
  private readonly ILogger<TeklaBasicConnectorBinding> _logger;
  private readonly TSM.Model _model;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  public TeklaBasicConnectorBinding(
    IBrowserBridge parent,
    ISpeckleApplication speckleApplication,
    DocumentModelStore store,
    ILogger<TeklaBasicConnectorBinding> logger,
    TSM.Model model,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    _speckleApplication = speckleApplication;
    _store = store;
    Parent = parent;
    _logger = logger;
    _model = model;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    Commands = new BasicConnectorBindingCommands(parent);
    _store.DocumentChanged += (_, _) =>
      _topLevelExceptionHandler.FireAndForget(async () =>
      {
        await Commands.NotifyDocumentChanged();
      });
  }

  public string GetSourceApplicationName() => _speckleApplication.Slug;

  public string GetSourceApplicationVersion() => _speckleApplication.HostApplicationVersion;

  public string GetConnectorVersion() => _speckleApplication.SpeckleVersion;

  public DocumentInfo GetDocumentInfo() =>
    new(_model.GetInfo().ModelPath, _model.GetInfo().ModelName, _model.GetInfo().GetHashCode().ToString());

  public DocumentModelStore GetDocumentState() => _store;

  public void AddModel(ModelCard model) => _store.AddModel(model);

  public void UpdateModel(ModelCard model) => _store.UpdateModel(model);

  public void RemoveModel(ModelCard model) => _store.RemoveModel(model);

  public void RemoveModels(List<ModelCard> models) => _store.RemoveModels(models);

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
        await Commands.SetModelError(modelCardId, new OperationCanceledException("No objects found to highlight."));
        return;
      }

      await HighlightObjects(objectIds);
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "Failed to highlight model");
      await Commands.SetModelError(modelCardId, ex);
    }
  }

  public async Task HighlightObjects(IReadOnlyList<string> objectIds)
  {
    try
    {
      await Task.Run(() =>
      {
        // passing an empty list to create current selection
        var selector = new TSMUI.ModelObjectSelector();
        selector.Select(new ArrayList());

        if (objectIds.Count > 0)
        {
          var modelObjects = objectIds
            .Select(id => _model.SelectModelObject(new Identifier(new Guid(id))))
            .Where(obj => obj != null)
            .ToList();

          selector.Select(new ArrayList(modelObjects));

          // to find the min and max coordinates of the selected objects
          // with that we can create a bounding box and zoom selected
          var points = new List<Point>();
          foreach (var obj in modelObjects)
          {
            points.Add(obj.GetCoordinateSystem().Origin);
            foreach (TSM.ModelObject child in obj.GetChildren())
            {
              points.Add(child.GetCoordinateSystem().Origin);
            }
          }

          var minX = points.Min(p => p.X);
          var minY = points.Min(p => p.Y);
          var minZ = points.Min(p => p.Z);
          var maxX = points.Max(p => p.X);
          var maxY = points.Max(p => p.Y);
          var maxZ = points.Max(p => p.Z);

          // create the bounding box
          var bounds = new AABB { MinPoint = new Point(minX, minY, minZ), MaxPoint = new Point(maxX, maxY, maxZ) };

          // zoom in to bounding box
          TSMUI.ViewHandler.ZoomToBoundingBox(bounds);
        }
        _model.CommitChanges();
      });
    }
    catch (InvalidOperationException ex)
    {
      _logger.LogError(ex, "Failed to highlight objects");
    }
  }
}
