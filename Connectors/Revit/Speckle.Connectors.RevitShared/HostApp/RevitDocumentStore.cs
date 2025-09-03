using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Utils;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk;
using Speckle.Sdk.Common;
using Speckle.Sdk.SQLite;

namespace Speckle.Connectors.Revit.HostApp;

// POC: should be interfaced out
internal sealed class RevitDocumentStore : DocumentModelStore
{
  private readonly ILogger<RevitDocumentStore> _logger;
  private readonly IAppIdleManager _idleManager;
  private readonly RevitContext _revitContext;
  private readonly DocumentModelStorageSchema _documentModelStorageSchema;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly ISqLiteJsonCacheManager _jsonCacheManager;

  public RevitDocumentStore(
    IAppIdleManager idleManager,
    RevitContext revitContext,
    IJsonSerializer jsonSerializer,
    DocumentModelStorageSchema documentModelStorageSchema,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    IRevitTask revitTask,
    ISqLiteJsonCacheManagerFactory jsonCacheManagerFactory,
    ILogger<RevitDocumentStore> logger
  )
    : base(logger, jsonSerializer)
  {
    _jsonCacheManager = jsonCacheManagerFactory.CreateForUser("ConnectorsFileData");
    _idleManager = idleManager;
    _revitContext = revitContext;
    _documentModelStorageSchema = documentModelStorageSchema;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _logger = logger;

    UIApplication uiApplication = _revitContext.UIApplication.NotNull();

    revitTask.Run(() =>
    {
      uiApplication.ViewActivated += (s, e) => _topLevelExceptionHandler.CatchUnhandled(() => OnViewActivated(s, e));

      uiApplication.Application.DocumentOpening += (_, _) =>
        _topLevelExceptionHandler.CatchUnhandled(() => IsDocumentInit = false);

      uiApplication.Application.DocumentOpened += (_, _) =>
        _topLevelExceptionHandler.CatchUnhandled(() => IsDocumentInit = false);

      // There is no event that we can hook here for double-click file open...
      // It is kind of harmless since we create this object as "SingleInstance".
      LoadState();
      OnDocumentChanged();
    });
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
      nameof(LoadState) + nameof(OnDocumentChanged),
      () =>
      {
        LoadState();
        OnDocumentChanged();
      }
    );
  }

  protected override void HostAppSaveState(string modelCardState)
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    // POC: this can happen? A: Not really, imho (dim) (Adam seyz yes it can if loading also triggers a save)
    if (document == null)
    {
      return;
    }

    try
    {
      var key = document.CreationGUID.ToString();
      _jsonCacheManager.UpdateObject(key, modelCardState);
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      var key = document.CreationGUID.ToString();
      _logger.LogError(ex, "Failed to save model card state for document {DocumentId}", key);
    }
  }

  protected override void LoadState()
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    // POC: this can happen? A: Not really, imho (dim) (Adam seyz yes it can if loading also triggers a save)
    if (document == null)
    {
      return;
    }

    var stateEntity = GetSpeckleEntity(document);
    if (stateEntity == null || !stateEntity.IsValid())
    {
      ClearAndSave();
      return;
    }

    var key = document.CreationGUID.ToString();
    var state = _jsonCacheManager.GetObject(key);
    LoadFromString(state);
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
