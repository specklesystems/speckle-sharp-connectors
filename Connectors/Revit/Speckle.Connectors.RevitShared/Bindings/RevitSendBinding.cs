using System.IO;
using Autodesk.Revit.DB;
using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.Common.Cancellation;
using Speckle.Connectors.Common.Threading;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Bridge;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Settings;
using Speckle.Connectors.Revit.HostApp;
using Speckle.Connectors.Revit.Operations.Send.Settings;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.Bindings;

internal sealed class RevitSendBinding : RevitBaseBinding, ISendBinding
{
  private readonly RevitContext _revitContext;
  private readonly DocumentModelStore _store;
  private readonly ICancellationManager _cancellationManager;
  private readonly ISendConversionCache _sendConversionCache;

  private readonly ToSpeckleSettingsManager _toSpeckleSettingsManager;
  private readonly IRevitConversionSettingsFactory _revitConversionSettingsFactory;
  private readonly RevitToSpeckleCacheSingleton _revitToSpeckleCacheSingleton;
  private readonly ITopLevelExceptionHandler _topLevelExceptionHandler;
  private readonly LinkedModelHandler _linkedModelHandler;
  private readonly RoomsAndAreasHandler _roomsAndAreasHandler;
  private readonly IThreadContext _threadContext;
  private readonly ISendOperationManagerFactory _sendOperationManagerFactory;
  private readonly ParameterUpdater _parameterUpdater;
  private readonly RevitSendChangeTracker _changeTracker;
  private bool _isDocChangedSubscribed;
  private EventHandler<Autodesk.Revit.DB.Events.DocumentChangedEventArgs>? _documentChangedHandler;
  private readonly ConnectorConfig _config;

  public RevitSendBinding(
    RevitContext revitContext,
    DocumentModelStore store,
    ICancellationManager cancellationManager,
    IBrowserBridge bridge,
    ISendConversionCache sendConversionCache,
    ToSpeckleSettingsManager toSpeckleSettingsManager,
    IRevitConversionSettingsFactory revitConversionSettingsFactory,
    RevitToSpeckleCacheSingleton revitToSpeckleCacheSingleton,
    ITopLevelExceptionHandler topLevelExceptionHandler,
    LinkedModelHandler linkedModelHandler,
    RoomsAndAreasHandler roomsAndAreasHandler,
    IThreadContext threadContext,
    IRevitTask revitTask,
    ISendOperationManagerFactory sendOperationManagerFactory,
    ParameterUpdater parameterUpdater,
    IConfigStore configStore,
    RevitSendChangeTracker changeTracker
  )
    : base("sendBinding", bridge)
  {
    _revitContext = revitContext;
    _store = store;
    _cancellationManager = cancellationManager;
    _sendConversionCache = sendConversionCache;
    _toSpeckleSettingsManager = toSpeckleSettingsManager;
    _revitConversionSettingsFactory = revitConversionSettingsFactory;
    _revitToSpeckleCacheSingleton = revitToSpeckleCacheSingleton;
    _topLevelExceptionHandler = topLevelExceptionHandler;
    _linkedModelHandler = linkedModelHandler;
    _roomsAndAreasHandler = roomsAndAreasHandler;
    _threadContext = threadContext;
    _sendOperationManagerFactory = sendOperationManagerFactory;
    _parameterUpdater = parameterUpdater;
    _changeTracker = changeTracker;
    _config = configStore.GetConnectorConfig();

    Commands = new SendBindingUICommands(bridge);
    _changeTracker.Initialize(Commands, RefreshSenderForActiveDocument);
    // TODO expiry events
    // TODO filters need refresh events

    revitTask.Run(() =>
    {
      _documentChangedHandler = (_, e) =>
        _topLevelExceptionHandler.CatchUnhandled(() => _changeTracker.HandleDocChange(e));
      _store.ModelCardsChanged += (_, e) => OnModelCardsChanged(e);
      _store.DocumentChanged += (_, _) => topLevelExceptionHandler.FireAndForget(async () => await OnDocumentChanged());
    });
  }

  private void OnModelCardsChanged(ModelCardsChangedEventArgs e)
  {
    if (
      !_config.DocumentChangeListeningDisabled
      && e.ModelCards.Count > 0
      && e.ModelCards.Any(m => m.TypeDiscriminator == nameof(SenderModelCard))
    )
    {
      SubscribeDocChanged();
    }
    else
    {
      UnsubscribeDocChanged();
    }
  }

  private void SubscribeDocChanged()
  {
    if (_documentChangedHandler == null || _isDocChangedSubscribed)
    {
      return;
    }

    _threadContext.RunOnMain(() =>
    {
      _revitContext.UIApplication.NotNull().Application.DocumentChanged += _documentChangedHandler;
    });
    _isDocChangedSubscribed = true;
  }

  private void UnsubscribeDocChanged()
  {
    if (_documentChangedHandler == null || !_isDocChangedSubscribed)
    {
      return;
    }

    _threadContext.RunOnMain(() =>
    {
      _revitContext.UIApplication.NotNull().Application.DocumentChanged -= _documentChangedHandler;
    });
    _isDocChangedSubscribed = false;
  }

  public List<ISendFilter> GetSendFilters() =>
    [
      new RevitSelectionFilter { IsDefault = true },
      new RevitViewsFilter(_revitContext),
      new RevitCategoriesFilter(_revitContext),
    ];

  public List<ICardSetting> GetSendSettings() =>
    [
      new DetailLevelSetting(),
      new SendReferencePointSetting(),
      new SendParameterNullOrEmptyStringsSetting(),
      new LinkedModelsSetting(),
      new SendRebarsAsVolumetricSetting(),
      new SendAreasAsMeshSetting(),
      new AppendRoomsAndAreasSetting(),
    ];

  public void CancelSend(string modelCardId) => _cancellationManager.CancelOperation(modelCardId);

  public SendBindingUICommands Commands { get; }

  public async Task Send(string modelCardId)
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null)
    {
      throw new SpeckleException("No document is active for sending.");
    }
    using var manager = _sendOperationManagerFactory.Create();
    var (fileName, fileBytes) = GetFileInfo(document);
    await manager.Process<DocumentToConvert>(
      Commands,
      modelCardId,
      (sp, card) =>
      {
        sp.GetRequiredService<IConverterSettingsStore<RevitConversionSettings>>()
          .Initialize(
            _revitConversionSettingsFactory.Create(
              _toSpeckleSettingsManager.GetDetailLevelSetting(document, card),
              _toSpeckleSettingsManager.GetReferencePointSetting(document, card),
              _toSpeckleSettingsManager.GetSendParameterNullOrEmptyStringsSetting(document, card),
              _toSpeckleSettingsManager.GetLinkedModelsSetting(document, card),
              _toSpeckleSettingsManager.GetSendRebarsAsVolumetric(document, card),
              _toSpeckleSettingsManager.GetSendAreasAsMesh(document, card),
              false
            )
          );
      },
      async x => await RefreshElementsIdsOnSender(document, x.NotNull()),
      fileName: fileName,
      fileSizeBytes: fileBytes
    );
  }

  public async Task UpdateParameters(List<ParameterChangeRequest> changes)
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null)
    {
      throw new SpeckleException("No document is active.");
    }

    await _threadContext.RunOnMainAsync(() =>
    {
      using var transaction = new Transaction(document, "Speckle Parameter Updates");
      transaction.Start();

      foreach (var change in changes)
      {
        var element = document.GetElement(change.ApplicationId);
        if (element == null)
        {
          continue;
        }

        var path = ParsePath(change.Path);
        var result = _parameterUpdater.Update(element, path, change.To);
      }

      transaction.Commit();
      return Task.FromResult(true);
    });
  }

  private string[] ParsePath(string concatenatedPath)
  {
    // "properties.Parameters.Type Parameters.Other.Family Name"
    //  → ["Type Parameters", "Other", "Family Name"]
    var segments = concatenatedPath.Split('.');
    return segments.Skip(2).ToArray();
  }

  private static (string? fileName, long? fileBytes) GetFileInfo(Document document)
  {
    string fullPath = document.PathName;
    if (File.Exists(document.PathName))
    {
      var fileInfo = new FileInfo(document.PathName);
      return (fileInfo.Name, fileInfo.Length);
    }
    else
    {
      return (fullPath.Split('/').LastOrDefault(), null);
    }
  }

  private async Task<List<DocumentToConvert>> RefreshElementsIdsOnSender(Document document, SenderModelCard modelCard)
  {
    if (modelCard.SendFilter.NotNull() is IRevitSendFilter viewFilter)
    {
      viewFilter.SetContext(_revitContext);
    }

    var selectedObjects = await _threadContext.RunOnMainAsync(() =>
      Task.FromResult(modelCard.SendFilter.NotNull().RefreshObjectIds())
    );

    var allElements = selectedObjects.Select(uid => document.GetElement(uid)).Where(el => el is not null).ToList();

    // split elements between main model and linked models
    var elementsOnMainModel = allElements.Where(el => el is not RevitLinkInstance).ToList();
    var linkedModels = allElements.OfType<RevitLinkInstance>().ToList();

    // should ideally reuse the initialized value from the scoped IConverterSettingsStore<RevitConversionSettings>.
    // but, it's scoped and to avoid bigger scarier changes I'm re-fetching the setting here (inexpensive operation?)
    Transform? mainModelTransform = _toSpeckleSettingsManager.GetReferencePointSetting(document, modelCard);
    List<DocumentToConvert> documentElementContexts = [new(mainModelTransform, document, elementsOnMainModel)];

    // get the linked models setting - this decision belongs at this level
    bool includeLinkedModels = _toSpeckleSettingsManager.GetLinkedModelsSetting(document, modelCard);

    // ⚠️ process linked models - RevitSendBinding controls the flow based on settings!
    // If setting not enabled, we won't unpack (see if-else block)
    if (linkedModels.Count > 0)
    {
      var linkedDocumentContexts = new List<DocumentToConvert>();

      foreach (var linkedModel in linkedModels)
      {
        var linkedDoc = linkedModel.GetLinkDocument();
        if (linkedDoc == null)
        {
          continue;
        }

        // transform maps linked model elements into the main model's reference point coordinate system
        // first apply the user's reference point transform (setting) then adjust for the linked model's placement relative to host.
        Transform transform = (mainModelTransform ?? Transform.Identity).Multiply(
          linkedModel.GetTotalTransform().Inverse
        );

        // decision about whether to process elements is made here, not in the handler
        // only collects elements from linked models when the setting is enabled
        if (includeLinkedModels)
        {
          // handler is only responsible for element collection mechanics
          var linkedElements = _linkedModelHandler.GetLinkedModelElements(
            document,
            modelCard.SendFilter,
            linkedDoc,
            transform
          );
          linkedDocumentContexts.Add(new(transform, linkedDoc, linkedElements));
        }
        // ⚠️ when disabled, still adds empty contexts to maintain warning generation in RevitRootObjectBuilder
        // this approach (to signal that warnings are needed) relies on empty element lists which smells and is a bit of an implicit mechanism
        // buuuuut, it works (for now 👀).
        else
        {
          linkedDocumentContexts.Add(new(transform, linkedDoc, new List<Element>()));
        }
      }
      documentElementContexts.AddRange(linkedDocumentContexts);
    }

    // append rooms and/or areas from the whole document when requested, independent of the active filter
    //TODO settings should be configured per filter. This setting is only for view filter when selected view is a 3d view.
    var roomsAndAreasMode = _toSpeckleSettingsManager.GetAppendRoomsAndAreas(document, modelCard);
    if (roomsAndAreasMode != AppendRoomsAndAreasMode.None)
    {
      var existingIds = elementsOnMainModel.Select(e => e.UniqueId).ToHashSet();
      elementsOnMainModel.AddRange(
        _roomsAndAreasHandler
          .CollectRoomsAndAreas(document, roomsAndAreasMode)
          .Where(e => !existingIds.Contains(e.UniqueId))
      );
    }

    // update ID map
    if (modelCard.SendFilter is not null && modelCard.SendFilter.IdMap is not null)
    {
      var newSelectedObjectIds = new List<string>();
      foreach (Element element in allElements)
      {
        modelCard.SendFilter.IdMap[element.Id.ToString()] = element.UniqueId;
        newSelectedObjectIds.Add(element.UniqueId);
      }

      // NOTE: preserve & persist original user selection for selection filter implemented during
      // [CNX-2400](https://linear.app/speckle/issue/CNX-2400/object-dont-update-on-publish)
      // NOTE: update with current document for views and categories filter since these represent dynamic queries
      // View & categories filters self-update their SelectedObjectIds in RefreshObjectIds(), maintaining consistency
      var objectIds =
        modelCard.SendFilter is RevitSelectionFilter ? modelCard.SendFilter.SelectedObjectIds : newSelectedObjectIds;
      await Commands.SetFilterObjectIds(modelCard.ModelCardId.NotNull(), modelCard.SendFilter.IdMap, objectIds);
    }

    return documentElementContexts;
  }

  private async Task RefreshSenderForActiveDocument(SenderModelCard sender)
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null)
    {
      return;
    }
    await RefreshElementsIdsOnSender(document, sender);
  }

  // POC: Will be re-addressed later with better UX with host apps that are friendly on async doc operations.
  // That's why don't bother for now how to get rid of from dup logic in other bindings.
  private async Task OnDocumentChanged()
  {
    _sendConversionCache.ClearCache();
    _revitToSpeckleCacheSingleton.ClearCache();

    if (_cancellationManager.NumberOfOperations > 0)
    {
      _cancellationManager.CancelAllOperations();
      await Commands.SetGlobalNotification(
        ToastNotificationType.INFO,
        "Document Switch",
        "Operations cancelled because of document swap!"
      );
    }
  }
}
