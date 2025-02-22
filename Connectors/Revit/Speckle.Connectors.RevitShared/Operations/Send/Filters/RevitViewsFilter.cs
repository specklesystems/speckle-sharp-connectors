﻿using Autodesk.Revit.DB;
using Speckle.Connectors.DUI.Exceptions;
using Speckle.Connectors.DUI.Models.Card.SendFilter;
using Speckle.Connectors.DUI.Utils;
using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public class RevitViewsFilter : DiscriminatedObject, ISendFilterSelect, IRevitSendFilter
{
  private RevitContext _revitContext;
  private Document? _doc;
  public string Id { get; set; } = "revitViews";
  public string Name { get; set; } = "Views";
  public string? Summary { get; set; }
  public bool IsDefault { get; set; }
  public string? SelectedView { get; set; }
  public List<string> SelectedObjectIds { get; set; }
  public Dictionary<string, string>? IdMap { get; set; } = new();
  public List<string>? AvailableViews { get; set; }

  public bool IsMultiSelectable { get; set; }
  public List<SendFilterSelectItem> SelectedItems { get; set; }
  public List<SendFilterSelectItem> Items { get; set; }

  public RevitViewsFilter() { }

  public RevitViewsFilter(RevitContext revitContext)
  {
    IsMultiSelectable = false;
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;

    GetViews();
  }

  public View? GetView()
  {
    if (SelectedItems is null)
    {
      return null;
    }
    if (SelectedItems.Count == 0)
    {
      return null;
    }
    string[] result = SelectedItems.First().Name.Split(new string[] { " - " }, 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(_doc);
    return collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.ViewType.ToString().Equals(viewFamilyString) && v.Name.Equals(viewString));
  }

  /// <summary>
  /// Always need to run on Revit UI thread (main) because of FilteredElementCollector.
  /// Use it with APIContext.Run
  /// </summary>
  /// <exception cref="SpeckleSendFilterException">Whenever no view is found.</exception>
  public List<string> RefreshObjectIds()
  {
    var objectIds = new List<string>();
    if (SelectedItems is null)
    {
      return objectIds;
    }
    if (SelectedItems.Count == 0)
    {
      return objectIds;
    }

    // Paşa Bilal wants it like this... (three dots = important meaning for ogu)
    string[] result = SelectedItems.First().Name.Split(new string[] { " - " }, 2, StringSplitOptions.None);
    var viewFamilyString = result[0];
    var viewString = result[1];

    using var collector = new FilteredElementCollector(_doc);
    View? view = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .FirstOrDefault(v => v.ViewType.ToString().Equals(viewFamilyString) && v.Name.Equals(viewString));

    if (view is null)
    {
      throw new SpeckleSendFilterException("View not found, please update your model send filter.");
    }
    using var viewCollector = new FilteredElementCollector(_doc, view.Id);
    List<Element> elementsInView = viewCollector.ToElements().ToList();
    objectIds = elementsInView.Select(e => e.UniqueId).ToList();
    SelectedObjectIds = objectIds;
    return objectIds;
  }

  private void GetViews()
  {
    using var collector = new FilteredElementCollector(_doc);
    var views = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .Where(v => !v.IsTemplate)
      .Where(v => !v.IsAssemblyView)
      .Where(v =>
        v.ViewType
          is ViewType.FloorPlan
            or ViewType.Elevation
            or ViewType.Rendering
            or ViewType.Section
            or ViewType.ThreeD
            or ViewType.Detail
            or ViewType.CeilingPlan
            or ViewType.AreaPlan
      )
      .Select(v => v.ViewType.ToString() + " - " + v.Name.ToString())
      .ToList();
    var viewItems = collector
      .OfClass(typeof(View))
      .Cast<View>()
      .Where(v => !v.IsTemplate)
      .Where(v => !v.IsAssemblyView)
      .Where(v =>
        v.ViewType
          is ViewType.FloorPlan
            or ViewType.Elevation
            or ViewType.Rendering
            or ViewType.Section
            or ViewType.ThreeD
            or ViewType.Detail
            or ViewType.CeilingPlan
            or ViewType.AreaPlan
      )
      .Select(v => new SendFilterSelectItem(v.UniqueId.ToString(), v.ViewType + " - " + v.Name.ToString()))
      .ToList();
    Items = viewItems;
    AvailableViews = views;
  }

  /// <summary>
  /// NOTE: this is needed since we need doc on `GetObjectIds()` function after it deserialized.
  /// DI doesn't help here to pass RevitContext from constructor.
  /// </summary>
  public void SetContext(RevitContext revitContext)
  {
    _revitContext = revitContext;
    _doc = _revitContext.UIApplication?.ActiveUIDocument.Document;
  }
}
