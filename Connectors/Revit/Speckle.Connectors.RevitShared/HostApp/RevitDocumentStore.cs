using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI.Events;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Eventing;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.Revit.HostApp;

// POC: should be interfaced out
internal sealed class RevitDocumentStore : DocumentModelStore
{
  // POC: move to somewhere central?
  private static readonly Guid s_revitDocumentStoreId = new("D35B3695-EDC9-4E15-B62A-D3FC2CB83FA3");

  private readonly IRevitContext _revitContext;
  private readonly DocumentModelStorageSchema _documentModelStorageSchema;
  private readonly IdStorageSchema _idStorageSchema;
  private readonly IEventAggregator _eventAggregator;

  public RevitDocumentStore(
    IRevitContext revitContext,
    IJsonSerializer jsonSerializer,
    DocumentModelStorageSchema documentModelStorageSchema,
    IdStorageSchema idStorageSchema,
    IEventAggregator eventAggregator
  )
    : base(jsonSerializer)
  {
    _revitContext = revitContext;
    _documentModelStorageSchema = documentModelStorageSchema;
    _idStorageSchema = idStorageSchema;
    _eventAggregator = eventAggregator;

    eventAggregator.GetEvent<DocumentOpenedEvent>().Subscribe(OnDocumentOpen);
    eventAggregator.GetEvent<DocumentOpeningEvent>().Subscribe(OnDocumentOpen);
    eventAggregator.GetEvent<ViewActivatedEvent>().Subscribe(OnViewActivated);

    // There is no event that we can hook here for double-click file open...
    // It is kind of harmless since we create this object as "SingleInstance".

    RevitThreadContext.Run(LoadState).FireAndForget();
  }

  private void OnDocumentOpen(object _) => IsDocumentInit = false;

  public override Task OnDocumentStoreInitialized() =>
    _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());

  /// <summary>
  /// This is the place where we track document switch for new document -> Responsible to Read from new doc
  /// </summary>
  private void OnViewActivated(ViewActivatedEventArgs e)
  {
    if (e.Document == null)
    {
      return;
    }

    // Return only if we are switching views that belongs to same document
    if (e.PreviousActiveView?.Document != null && e.PreviousActiveView.Document.Equals(e.CurrentActiveView.Document))
    {
      return;
    }

    IsDocumentInit = true;
    _eventAggregator.GetEvent<IdleEvent>().OneTimeSubscribe(nameof(RevitDocumentStore), OnIdleEvent);
  }

  private async Task OnIdleEvent(object _)
  {
    LoadState();
    await _eventAggregator.GetEvent<DocumentStoreChangedEvent>().PublishAsync(new object());
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    // POC: this can happen? A: Not really, imho (dim) (Adam seyz yes it can if loading also triggers a save)
    if (doc == null)
    {
      return;
    }

    RevitThreadContext
      .Run(() =>
      {
        using Transaction t = new(doc, "Speckle Write State");
        t.Start();
        using DataStorage ds = GetSettingsDataStorage(doc) ?? DataStorage.Create(doc);

        using Entity stateEntity = new(_documentModelStorageSchema.GetSchema());
        string serializedModels = Serialize();
        stateEntity.Set("contents", serializedModels);

        using Entity idEntity = new(_idStorageSchema.GetSchema());
        idEntity.Set("Id", s_revitDocumentStoreId);

        ds.SetEntity(idEntity);
        ds.SetEntity(stateEntity);
        t.Commit();
      })
      .FireAndForget();
  }

  protected override void LoadState()
  {
    var stateEntity = GetSpeckleEntity(_revitContext.UIApplication?.ActiveUIDocument?.Document);
    if (stateEntity == null || !stateEntity.IsValid())
    {
      ClearAndSave();
      return;
    }

    string modelsString = stateEntity.Get<string>("contents");
    LoadFromString(modelsString);
  }

  private DataStorage? GetSettingsDataStorage(Document doc)
  {
    using FilteredElementCollector collector = new(doc);
    FilteredElementCollector dataStorages = collector.OfClass(typeof(DataStorage));

    foreach (Element element in dataStorages)
    {
      DataStorage dataStorage = (DataStorage)element;
      Entity settingIdEntity = dataStorage.GetEntity(_idStorageSchema.GetSchema());
      if (!settingIdEntity.IsValid())
      {
        continue;
      }

      Guid id = settingIdEntity.Get<Guid>("Id");
      if (!id.Equals(s_revitDocumentStoreId))
      {
        continue;
      }

      return dataStorage;
    }

    return null;
  }

  private Entity? GetSpeckleEntity(Document? doc)
  {
    if (doc is null)
    {
      return null;
    }
    using FilteredElementCollector collector = new(doc);

    FilteredElementCollector dataStorages = collector.OfClass(typeof(DataStorage));
    foreach (Element element in dataStorages)
    {
      DataStorage dataStorage = (DataStorage)element;
      Entity settingEntity = dataStorage.GetEntity(_documentModelStorageSchema.GetSchema());
      if (!settingEntity.IsValid())
      {
        continue;
      }

      return settingEntity;
    }

    return null;
  }
}
