using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Revit.Async;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.HostApp;

// POC: should be interfaced out
internal sealed class RevitDocumentStore : DocumentModelStore
{
  // POC: move to somewhere central?
  private static readonly Guid s_revitDocumentStoreId = new("D35B3695-EDC9-4E15-B62A-D3FC2CB83FA3");

  private readonly RevitContext _revitContext;
  private readonly IRevitIdleManager _idleManager;
  private readonly DocumentModelStorageSchema _documentModelStorageSchema;
  private readonly IdStorageSchema _idStorageSchema;

  public RevitDocumentStore(
    IRevitIdleManager idleManager,
    RevitContext revitContext,
    IJsonSerializer jsonSerializer,
    DocumentModelStorageSchema documentModelStorageSchema,
    IdStorageSchema idStorageSchema,
    ITopLevelExceptionHandler topLevelExceptionHandler
  )
    : base(jsonSerializer)
  {
    _idleManager = idleManager;
    _revitContext = revitContext;
    _documentModelStorageSchema = documentModelStorageSchema;
    _idStorageSchema = idStorageSchema;

    _idleManager.RunAsync(() =>
    {
      UIApplication uiApplication = _revitContext.UIApplication.NotNull();

      uiApplication.ViewActivated += (s, e) => topLevelExceptionHandler.CatchUnhandled(() => OnViewActivated(s, e));

      uiApplication.Application.DocumentOpening += (_, _) =>
        topLevelExceptionHandler.CatchUnhandled(() => IsDocumentInit = false);

      uiApplication.Application.DocumentOpened += (_, _) =>
        topLevelExceptionHandler.CatchUnhandled(() => IsDocumentInit = false);
    });

    // There is no event that we can hook here for double-click file open...
    // It is kind of harmless since we create this object as "SingleInstance".
    LoadState();
    OnDocumentChanged();
  }

  /// <summary>
  /// This is the place where we track document switch for new document -> Responsible to Read from new doc
  /// </summary>
  private void OnViewActivated(object? _, ViewActivatedEventArgs e)
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
    _idleManager.SubscribeToIdle(
      nameof(RevitDocumentStore),
      () =>
      {
        LoadState();
        OnDocumentChanged();
      }
    );
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    // POC: this can happen? A: Not really, imho (dim) (Adam seyz yes it can if loading also triggers a save)
    if (doc == null)
    {
      return;
    }
    RevitTask.RunAsync(() =>
    {
      var doc = (_revitContext.UIApplication?.ActiveUIDocument?.Document).NotNull();
      using Transaction t = new(doc, "Speckle Write State");
      t.Start();
      using DataStorage ds = GetSettingsDataStorage(doc) ?? DataStorage.Create(doc);

      using Entity stateEntity = new(_documentModelStorageSchema.GetSchema());
      stateEntity.Set("contents", modelCardState);

      using Entity idEntity = new(_idStorageSchema.GetSchema());
      idEntity.Set("Id", s_revitDocumentStoreId);

      ds.SetEntity(idEntity);
      ds.SetEntity(stateEntity);
      t.Commit();
    });
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
