using System.Collections.ObjectModel;
using System.Diagnostics;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Utils;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Models;

/// <summary>
/// Encapsulates the state Speckle needs to persist in the host app's document.
/// </summary>
public abstract class DocumentModelStore
{
  private readonly SuspendingNotifyCollection<ModelCard> _models = new();

  /// <summary>
  /// Stores all the model cards in the current document/file.
  /// </summary>
  public IReadOnlyNotifyCollection<ModelCard> Models => _models;

  private readonly JsonSerializerSettings _serializerOptions;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;

  /// <summary>
  /// Base host app state class that controls the storage of the models in the file.
  /// </summary>
  /// <param name="serializerOptions">our custom serialiser that should be globally DI'ed in.</param>
  protected DocumentModelStore(
    JsonSerializerSettings serializerOptions,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
  {
    _serializerOptions = serializerOptions;
    _topLevelExceptionHandler = topLevelExceptionHandler;

    RegisterWriteOnChangeEvent();
  }

  private void RegisterWriteOnChangeEvent()
  {
    lock (_models)
    {
      _models.CollectionChanged += (_, _) => _topLevelExceptionHandler.CatchUnhandled(WriteToFile);
    }
  }

  /// <summary>
  /// This event is triggered by each specific host app implementation of the document model store.
  /// </summary>
  // POC: unsure about the PublicAPI annotation, unsure if this changed handle should live here on the store...  :/
  public event EventHandler? DocumentChanged;

  public virtual bool IsDocumentInit { get; set; }

  // TODO: not sure about this, throwing an exception, needs some thought...
  // Further note (dim): If we reach to the stage of throwing an exception here because a model is not found, there's a huge misalignment between the UI's list of model cards and the host app's.
  // In theory this should never really happen, but if it does
  public ModelCard GetModelById(string id)
  {
    var model = Models.First(model => model.ModelCardId == id) ?? throw new ModelNotFoundException();
    return model;
  }

  public void AddModel(ModelCard model)
  {
    lock (_models)
    {
      _models.Add(model);
    }
  }

  public void AddRange(IEnumerable<ModelCard> models)
  {
    lock (_models)
    {
      _models.AddRange(models);
    }
  }

  public void Clear()
  {
    lock (_models)
    {
      using var sus = _models.SuspendNotifications();
      _models.Clear();
    }
  }

  public void UpdateModel(ModelCard model)
  {
    lock (_models)
    {
      int idx = Models.ToList().FindIndex(m => model.ModelCardId == m.ModelCardId);
      _models[idx] = model;
    }
  }

  public void RemoveModel(ModelCard model)
  {
    lock (_models)
    {
      _models.Remove(model);
    }
  }

  protected void OnDocumentChanged() => DocumentChanged?.Invoke(this, EventArgs.Empty);

  public IEnumerable<SenderModelCard> GetSenders() =>
    Models.Where(model => model.TypeDiscriminator == nameof(SenderModelCard)).Cast<SenderModelCard>();

  public IEnumerable<ReceiverModelCard> GetReceivers() =>
    Models.Where(model => model.TypeDiscriminator == nameof(ReceiverModelCard)).Cast<ReceiverModelCard>();

  protected string Serialize() => JsonConvert.SerializeObject(Models, _serializerOptions);

  // POC: this seemms more like a IModelsDeserializer?, seems disconnected from this class
  private ObservableCollection<ModelCard>? Deserialize(string models) =>
    JsonConvert.DeserializeObject<ObservableCollection<ModelCard>>(models, _serializerOptions);

  /// <summary>
  /// Implement this method according to the host app's specific ways of storing custom data in its file.
  /// </summary>
  public abstract void WriteToFile();

  /// <summary>
  /// Implement this method according to the host app's specific ways of reading custom data from its file.
  /// </summary>
  public abstract void ReadFromFile();

  protected void LoadFromString(string? models)
  {
    try
    {
      lock (_models)
      {
        using var sus = _models.SuspendNotifications();
        if (string.IsNullOrEmpty(models))
        {
          Clear();
          return;
        }
        AddRange(Deserialize(models.NotNull()).NotNull());
      }
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      Clear();
      Debug.WriteLine(ex.Message); // POC: Log here error and notify UI that cards not read succesfully
    }
  }
}
