using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.DUI.Utils;
using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Models;

/// <summary>
/// Encapsulates the state Speckle needs to persist in the host app's document.
/// </summary>
[GenerateAutoInterface]
public abstract class DocumentModelStore(ILogger<DocumentModelStore> logger, IJsonSerializer serializer)
  : IDocumentModelStore
{
  public event EventHandler<ModelCardsChangedEventArgs>? ModelCardsChanged;

  /// <summary>
  /// Raised when a <see cref="ReceiverModelCard"/> that has already been received (has <see cref="ReceiverModelCard.BakedObjectIds"/>)
  /// is updated with different settings.
  /// </summary>
  public event EventHandler<ReceiverSettingsChangedEventArgs>? ReceiverSettingsChanged;

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

  public virtual Task OnDocumentStoreInitialized() => Task.CompletedTask;

  public virtual bool IsDocumentInit { get; set; }

  // TODO: not sure about this, throwing an exception, needs some thought...
  // Further note (dim): If we reach to the stage of throwing an exception here because a model is not found, there's a huge misalignment between the UI's list of model cards and the host app's.
  // In theory this should never really happen, (Adam) but it does because of threading so don't throw (as said above)
  public ModelCard? GetModelById(string id)
  {
    lock (_models)
    {
      var model = _models.FirstOrDefault(model => model.ModelCardId == id);
      if (model is null)
      {
        logger.LogWarning("Model with id {id} not found", id);
        return null;
      }
      return model;
    }
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
    bool receiverSettingsChanged = false;
    lock (_models)
    {
      var index = _models.FindIndex(m => m.ModelCardId == model.ModelCardId);
      if (index == -1)
      {
        logger.LogWarning("Model card not found to update. Model card ID: {ModelCardId}", model.ModelCardId);
        return;
      }

      var oldModel = _models[index];
      receiverSettingsChanged =
        oldModel is ReceiverModelCard oldReceiver
        && model is ReceiverModelCard
        && oldReceiver.BakedObjectIds is { Count: > 0 }
        && HaveSettingsChanged(oldModel.Settings, model.Settings);

      _models[index] = model;
      SaveState();
    }

    if (receiverSettingsChanged)
    {
      ReceiverSettingsChanged?.Invoke(this, new ReceiverSettingsChangedEventArgs(model.ModelCardId.NotNull()));
    }
  }

  public void RemoveModel(ModelCard model)
  {
    lock (_models)
    {
      var index = _models.FindIndex(m => m.ModelCardId == model.ModelCardId);
      if (index == -1)
      {
        logger.LogWarning("Model card not found to update. Model card ID: {ModelCardId}", model.ModelCardId);
        return;
      }
      _models.RemoveAt(index);
      SaveState();
    }
  }

  public void RemoveModels(List<ModelCard> models)
  {
    lock (_models)
    {
      var listForMissingModelCards = new List<string>();
      foreach (var model in models)
      {
        var index = _models.FindIndex(m => m.ModelCardId == model.ModelCardId);
        if (index == -1)
        {
          listForMissingModelCards.Add(model.ModelCardId.NotNull());
        }
        _models.RemoveAt(index);
      }
      SaveState();
      if (listForMissingModelCards.Count > 0)
      {
        logger.LogWarning(
          "Model cards with IDs {ListForMissingModelCards} not found to remove",
          listForMissingModelCards
        );
      }
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

  protected string Serialize() => serializer.Serialize(Models.ToList());

  // POC: this seemms more like a IModelsDeserializer?, seems disconnected from this class
  protected List<ModelCard> Deserialize(string models) => serializer.Deserialize<List<ModelCard>>(models).NotNull();

  protected void SaveState()
  {
    lock (_models)
    {
      var state = Serialize();
      HostAppSaveState(state);
      ModelCardsChanged?.Invoke(this, new ModelCardsChangedEventArgs(_models.ToList().AsReadOnly()));
    }
  }

  /// <summary>
  /// Implement this method according to the host app's specific ways of reading custom data from its file.
  /// </summary>
  protected abstract void HostAppSaveState(string modelCardState);

  protected abstract void LoadState();

  private static bool HaveSettingsChanged(List<CardSetting>? oldSettings, List<CardSetting>? newSettings)
  {
    if (oldSettings is null && newSettings is null)
    {
      return false;
    }

    if (oldSettings is null || newSettings is null || oldSettings.Count != newSettings.Count)
    {
      return true;
    }

    foreach (var oldSetting in oldSettings)
    {
      var newSetting = newSettings.FirstOrDefault(s => s.Id == oldSetting.Id);
      if (newSetting is null)
      {
        return true;
      }

      if (!Equals(oldSetting.Value?.ToString(), newSetting.Value?.ToString()))
      {
        return true;
      }
    }

    return false;
  }

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
      logger.LogWarning(ex, "Failed to deserialize model cards from document");
    }
  }
}
