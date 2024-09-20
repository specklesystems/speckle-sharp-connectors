using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;
using Speckle.Sdk.Models.GraphTraversal;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// <para>On receive, this class will help structure atomic objects into nested revit groups based on the hierarchy that they're coming from. Expects to be a scoped dependency per receive operation.</para>
/// <para>How to use: during atomic object conversion, on each succesful conversion call <see cref="AddToGroupMapping"/>. Afterward, at the end of the recieve operation, call <see cref="BakeGroups"/> to actually create the groups in the revit document.</para>
/// </summary>
public class RevitGroupBaker : TraversalContextUnpacker
{
  private readonly IConverterSettingsStore<RevitConversionSettings> _converterSettings;
  private readonly RevitUtils _revitUtils;

  public RevitGroupBaker(IConverterSettingsStore<RevitConversionSettings> converterSettings, RevitUtils revitUtils)
  {
    _converterSettings = converterSettings;
    _revitUtils = revitUtils;
  }

  /// <summary>
  /// Adds the object to the correct group in preparation for <see cref="BakeGroups"/> at the end of the receive operation.
  /// </summary>
  /// <param name="traversalContext"></param>
  /// <param name="revitElement"></param>
  public void AddToGroupMapping(TraversalContext traversalContext, Element revitElement)
  {
    var collectionPath = GetCollectionPath(traversalContext);
    var currentLayerName = string.Empty;
    FakeGroup? previousGroup = null;
    var currentDepth = 0;

    foreach (var collection in collectionPath)
    {
      currentLayerName += collection.name + "-";
      if (_groupCache.TryGetValue(currentLayerName, out var g))
      {
        previousGroup = g;
        currentDepth++;
        continue;
      }

      var group = new FakeGroup()
      {
        // POC group names should be unique
        Name = _revitUtils.RemoveInvalidChars(collection.name),
        Depth = currentDepth++,
        Parent = previousGroup!
      };
      _groupCache[currentLayerName] = group;
      previousGroup = group;
    }

    previousGroup!.Ids.Add(revitElement.Id);
  }

  private readonly Dictionary<string, FakeGroup> _groupCache = new();

  /// <summary>
  /// Bakes the accumulated groups in Revit, with their objects.
  /// </summary>
  /// <param name="baseGroupName"></param>
  public void BakeGroups(string baseGroupName)
  {
    var orderedGroups = _groupCache.Values.OrderByDescending(group => group.Depth);
    Group? lastGroup = null;

    foreach (var group in orderedGroups)
    {
      var docGroup = _converterSettings.Current.Document.Create.NewGroup(group.Ids);
      group.Parent?.Ids.Add(docGroup.Id);
      docGroup.GroupType.Name = group.Name;
      lastGroup = docGroup;
    }

    lastGroup!.GroupType.Name = _revitUtils.RemoveInvalidChars(baseGroupName);
  }

  public void PurgeGroups(string baseGroupName)
  {
    var validBaseGroupName = _revitUtils.RemoveInvalidChars(baseGroupName);
    var document = _converterSettings.Current.Document;

    using (var collector = new FilteredElementCollector(document))
    {
      var groupIds = collector
        .OfClass(typeof(GroupType))
        .Where(g => g.Name == validBaseGroupName)
        .Select(g => g.Id)
        .ToList();

      document.Delete(groupIds);
    }
  }

  /// <summary>
  /// Little intermediate data structure that helps with the operations above.
  /// </summary>
  private sealed class FakeGroup
  {
    public List<ElementId> Ids { get; set; } = new();
    public int Depth { get; set; }
    public string Name { get; set; }
    public FakeGroup Parent { get; set; }
  }
}