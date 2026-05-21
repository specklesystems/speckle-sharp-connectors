using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;
using Speckle.Connectors.Common.Caching;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Models;
using Speckle.Connectors.DUI.Models.Card;
using Speckle.Connectors.Revit.Plugin;
using Speckle.Connectors.RevitShared.Operations.Send.Filters;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Owns the document-change → idle → expiration pipeline for the Revit send binding:
/// aggregates changed element ids, evicts the conversion cache on unit changes,
/// refreshes filters when views change, and marks sender model cards expired when
/// their tracked elements (or their types) are modified.
/// </summary>
internal sealed class RevitSendChangeTracker
{
  private readonly RevitIdleManager _revitIdleManager;
  private readonly RevitContext _revitContext;
  private readonly DocumentModelStore _store;
  private readonly ISendConversionCache _sendConversionCache;
  private readonly ElementUnpacker _elementUnpacker;

  private SendBindingUICommands? _commands;
  private Func<SenderModelCard, Task>? _refreshSender;

  // tracks doc → concatenated unit type ids, used to detect unit changes
  private readonly Dictionary<string, string> _docUnitCache = new();

  /// <summary>
  /// Used internally to aggregate the changed objects' id. Note we're using a concurrent dictionary here as the expiry check method is not thread safe, and this was causing problems. See:
  /// [CNX-202: Unhandled Exception Occurred when receiving in Rhino](https://linear.app/speckle/issue/CNX-202/unhandled-exception-occurred-when-receiving-in-rhino)
  /// As to why a concurrent dictionary, it's because it's the cheapest/easiest way to do so.
  /// https://stackoverflow.com/questions/18922985/concurrent-hashsett-in-net-framework
  /// </summary>
  private ConcurrentHashSet<ElementId> ChangedObjectIds { get; set; } = new();

  public RevitSendChangeTracker(
    RevitIdleManager revitIdleManager,
    RevitContext revitContext,
    DocumentModelStore store,
    ISendConversionCache sendConversionCache,
    ElementUnpacker elementUnpacker
  )
  {
    _revitIdleManager = revitIdleManager;
    _revitContext = revitContext;
    _store = store;
    _sendConversionCache = sendConversionCache;
    _elementUnpacker = elementUnpacker;
  }

  /// <summary>
  /// Wires the UI commands and per-sender refresh callback. Must be called by the owning
  /// binding once it has constructed its <see cref="SendBindingUICommands"/>.
  /// </summary>
  public void Initialize(SendBindingUICommands commands, Func<SenderModelCard, Task> refreshSender)
  {
    _commands = commands;
    _refreshSender = refreshSender;
  }

  /// <summary>
  /// Keeps track of the changed element ids as well as checks if any of them need to trigger
  /// a filter refresh (e.g., views being added).
  /// </summary>
  public void HandleDocChange(Autodesk.Revit.DB.Events.DocumentChangedEventArgs e)
  {
    ICollection<ElementId> modifiedElementIds = e.GetModifiedElementIds();
    var doc = e.GetDocument();
    if (doc == null)
    {
      return;
    }
    // NOTE: Whenever we save data into file this event also trigger changes on its DataStorage.
    // On every add/remove/update model attempt triggers this handler and was causing unnecessary calls on `RunExpirationChecks`
    // Re-check it once we implement Linked Documents
    if (modifiedElementIds.Count == 1)
    {
      if (modifiedElementIds.All(el => doc.GetElement(el) is DataStorage))
      {
        return;
      }
    }

    ICollection<ElementId> addedElementIds = e.GetAddedElementIds();
    ICollection<ElementId> deletedElementIds = e.GetDeletedElementIds();

    foreach (ElementId elementId in addedElementIds)
    {
      ChangedObjectIds.Add(elementId);
    }

    foreach (ElementId elementId in deletedElementIds)
    {
      ChangedObjectIds.Add(elementId);
    }

    foreach (ElementId elementId in modifiedElementIds)
    {
      ChangedObjectIds.Add(elementId);
    }

    if (addedElementIds.Count > 0)
    {
      _revitIdleManager.SubscribeToIdle(nameof(PostSetObjectIds), PostSetObjectIds);
    }

    if (HaveUnitsChanged(doc))
    {
      var objectIds = new List<string>();
      foreach (var sender in _store.GetSenders().ToList())
      {
        if (sender.SendFilter is null)
        {
          continue;
        }

        var selectedObjects = sender.SendFilter.NotNull().SelectedObjectIds;
        objectIds.AddRange(selectedObjects);
      }
      var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objectIds, doc);
      _sendConversionCache.EvictObjects(unpackedObjectIds);
    }

    _revitIdleManager.SubscribeToIdle(nameof(CheckFilterExpiration), CheckFilterExpiration);
    _revitIdleManager.SubscribeToIdle(nameof(RunExpirationChecks), RunExpirationChecks);
  }

  private bool HaveUnitsChanged(Document doc)
  {
    var docId = doc.Title + doc.PathName;
    var unitSpecTypeIds = new List<ForgeTypeId>() // list of units we care about
    {
      SpecTypeId.Angle,
      SpecTypeId.Area,
      SpecTypeId.Distance,
      SpecTypeId.Length,
      SpecTypeId.Volume,
    };
    var units = "";
    foreach (var typeId in unitSpecTypeIds)
    {
      units += doc.GetUnits().GetFormatOptions(typeId).GetUnitTypeId().TypeId;
    }

    if (_docUnitCache.TryGetValue(docId, out string? value))
    {
      if (value == units)
      {
        return false;
      }
      _docUnitCache[docId] = units;
      return true;
    }

    _docUnitCache[docId] = units;
    return false;
  }

  private async Task PostSetObjectIds()
  {
    var document = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (document == null || _refreshSender == null)
    {
      return;
    }
    foreach (var sender in _store.GetSenders().ToList())
    {
      await _refreshSender(sender);
    }
  }

  /// <summary>
  /// Notifies ui if any filters need refreshing. Currently, this only applies for view filters.
  /// </summary>
  private async Task CheckFilterExpiration()
  {
    // NOTE: below code seems like more make sense in terms of performance, but it causes unmanaged exception on Revit
    // using var viewCollector = new FilteredElementCollector(RevitContext.UIApplication?.ActiveUIDocument.Document);
    // var views = viewCollector.OfClass(typeof(View)).Cast<View>().Select(v => v.Id).ToList();
    // var intersection = ChangedObjectIds.Keys.Intersect(views).ToList();
    // if (intersection.Count != 0)
    // {
    //    await Commands.RefreshSendFilters();
    // }
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;
    if (doc == null || _commands == null)
    {
      return;
    }

    if (ChangedObjectIds.Any(e => doc.GetElement(e) is View))
    {
      await _commands.RefreshSendFilters();
    }
  }

  private async Task RunExpirationChecks()
  {
    var senders = _store.GetSenders().ToList();
    var doc = _revitContext.UIApplication?.ActiveUIDocument?.Document;

    if (doc == null || _commands == null)
    {
      return;
    }

    var objUniqueIds = new List<string>();
    var changedIds = ChangedObjectIds.ToList();

    // Handling type changes: if an element's type is changed, we need to mark as changed all objects that have that type.
    // Step 1: get any changed types
    var elementTypeIdsList = changedIds
      .Select(e => doc.GetElement(e))
      .OfType<ElementType>()
      .Select(el => el.Id)
      .ToHashSet(); // ToHashSet() for faster Contains

    // Step 2: Find all elements of the changed types, and add them to the changed ids list.
    if (elementTypeIdsList.Count != 0)
    {
      using var collector = new FilteredElementCollector(doc);
      var collectorElements = collector
        .WhereElementIsNotElementType()
        .Where(e => elementTypeIdsList.Contains(e.GetTypeId()));
      foreach (var elm in collectorElements)
      {
        changedIds.Add(elm.Id);
      }
    }

    foreach (var sender in senders)
    {
      foreach (var changedElementId in changedIds)
      {
        if (sender.SendFilter?.IdMap?.TryGetValue(changedElementId.ToString(), out var id) ?? false)
        {
          objUniqueIds.Add(id);
        }
      }
    }

    var unpackedObjectIds = _elementUnpacker.GetUnpackedElementIds(objUniqueIds, doc);
    _sendConversionCache.EvictObjects(unpackedObjectIds);

    // Note: we're doing object selection and card expiry management by old school ids
    List<string> expiredSenderIds = new();
    foreach (SenderModelCard modelCard in senders)
    {
      if (modelCard.SendFilter is IRevitSendFilter viewFilter)
      {
        viewFilter.SetContext(_revitContext);
      }

      if (modelCard.SendFilter is null || modelCard.SendFilter.IdMap is null)
      {
        continue;
      }

      var selectedObjects = modelCard.SendFilter.NotNull().IdMap.NotNull().Values;
      var intersection = selectedObjects.Intersect(objUniqueIds).ToList();
      bool isExpired = intersection.Count != 0;
      if (isExpired)
      {
        expiredSenderIds.Add(modelCard.ModelCardId.NotNull());
      }
    }

    await _commands.SetModelsExpired(expiredSenderIds);
    ChangedObjectIds = new();
  }
}
