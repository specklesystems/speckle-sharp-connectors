using System.Diagnostics;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Utils;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Models;

/// <summary>
/// Encapsulates the state Speckle needs to persist in the host app's document.
/// </summary>
public abstract class DocumentModelStore(IJsonSerializer serializer)
{
  private readonly List<ModelCard> _models = new();

  /// <summary>
  /// This event is triggered by each specific host app implementation of the document model store.
  /// </summary>
  // POC: unsure about the PublicAPI annotation, unsure if this changed handle should live here on the store...  :/
  public event EventHandler? DocumentChanged;

  //needed for javascript UI
  public IReadOnlyList<ModelCard> Models
  {
    get
    {
      lock (_models)
      {
        return _models.AsReadOnly();
      }
    }
  }

  protected void OnDocumentChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);

  public virtual bool IsDocumentInit { get; set; }

  // TODO: not sure about this, throwing an exception, needs some thought...
  // Further note (dim): If we reach to the stage of throwing an exception here because a model is not found, there's a huge misalignment between the UI's list of model cards and the host app's.
  // In theory this should never really happen, but if it does
  public ModelCard GetModelById(string id)
  {
    var model = _models.First(model => model.ModelCardId == id) ?? throw new ModelNotFoundException();
    return model;
  }

  public void AddModel(ModelCard model)
  {
    lock (_models)
    {
      _models.Add(model);
      SaveState();
    }
  }

  public void ClearAndSave()
  {
    lock (_models)
    {
      _models.Clear();
      SaveState();
    }
  }

  public void UpdateModel(ModelCard model)
  {
    lock (_models)
    {
      var index = _models.FindIndex(m => m.ModelCardId == model.ModelCardId);
      if (index == -1)
      {
        throw new ModelNotFoundException("Model card not found to update.");
      }
      _models[index] = model;
      SaveState();
    }
  }

  public void RemoveModel(ModelCard model)
  {
    lock (_models)
    {
      var index = _models.FindIndex(m => m.ModelCardId == model.ModelCardId);
      if (index == -1)
      {
        throw new ModelNotFoundException("Model card not found to update.");
      }
      _models.RemoveAt(index);
      SaveState();
    }
  }

  public IEnumerable<SenderModelCard> GetSenders()
  {
    lock (_models)
    {
      return _models
        .Where(model => model.TypeDiscriminator == nameof(SenderModelCard))
        .Cast<SenderModelCard>()
        .ToList();
    }
  }

  protected string Serialize() => serializer.Serialize(Models);

  // POC: this seemms more like a IModelsDeserializer?, seems disconnected from this class
  protected List<ModelCard> Deserialize(string models) => serializer.Deserialize<List<ModelCard>>(models).NotNull();

  protected void SaveState()
  {
    lock (_models)
    {
      var state = Serialize();
      HostAppSaveState(state);
    }
  }

  /// <summary>
  /// Implement this method according to the host app's specific ways of reading custom data from its file.
  /// </summary>
  protected abstract void HostAppSaveState(string modelCardState);

  protected abstract void LoadState();

  protected void LoadFromString(string? models)
  {
    try
    {
      lock (_models)
      {
        _models.Clear();
        if (string.IsNullOrEmpty(models))
        {
          return;
        }
        _models.AddRange(Deserialize(models.NotNull()).NotNull());
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      ClearAndSave();
      Debug.WriteLine(ex.Message); // POC: Log here error and notify UI that cards not read succesfully
    }
  }
}
