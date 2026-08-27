using Autodesk.Revit.DB;
using Speckle.Connectors.Common.Operations.Receive;
using Speckle.Converters.Common;
using Speckle.Converters.RevitShared.Settings;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Bakes all objects into a single top level group and pins it.
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

  private readonly List<ElementId> _elementIdsForTopLevelGroup = new();

  public void AddToTopLevelGroup(Element revitElement) => _elementIdsForTopLevelGroup.Add(revitElement.Id);

  public string? BakeGroupForTopLevel(string baseGroupName) =>
    BakeGroupForTopLevel(baseGroupName, _elementIdsForTopLevelGroup);

  /// <summary>Bakes <paramref name="elementIds"/> into one pinned, named top-level group and returns its UniqueId
  /// (null when there was nothing to group).</summary>
  /// <remarks>The explicit-collection overload exists for the artefact receive path, which threads its own member
  /// list through the bake rather than accumulating into this service's state.</remarks>
  public string? BakeGroupForTopLevel(string baseGroupName, IReadOnlyCollection<ElementId> elementIds)
  {
    if (elementIds.Count == 0)
    // if no elements were successfully converted, instead of throwing when creating a new group, we should just return and let object conversion exceptions bubble up.
    {
      return null;
    }

    var docGroup = _converterSettings.Current.Document.Create.NewGroup(elementIds.ToList());
    docGroup.GroupType.Name = _revitUtils.RemoveInvalidChars(baseGroupName);
    docGroup.Pinned = true;
    return docGroup.UniqueId;
  }

  /// <summary>Deletes one previously baked group (and its members) by UniqueId — the rename-proof counterpart to
  /// <see cref="PurgeGroups"/>, which can only find a group whose name still matches the current project/model.</summary>
  public void PurgeGroupById(string groupUniqueId)
  {
    var document = _converterSettings.Current.Document;
    if (document.GetElement(groupUniqueId) is not Group group)
    {
      return; // already gone, or the user ungrouped it — nothing to delete
    }

    var subgroupTypeIds = new List<ElementId>() { group.GroupType.Id };
    CollectSubGroupTypeIds(document, group, subgroupTypeIds);
    document.Delete(subgroupTypeIds);
  }

  public void PurgeGroups(string baseGroupName)
  {
    var document = _converterSettings.Current.Document;
    var groups = GetGroupsByName(document, baseGroupName);

    foreach (var group in groups)
    {
      var subgroupTypeIds = new List<ElementId>() { group.GroupType.Id };
      CollectSubGroupTypeIds(document, group, subgroupTypeIds);
      document.Delete(subgroupTypeIds);
    }
  }

  private List<Group> GetGroupsByName(Document doc, string groupName)
  {
    var validGroupName = _revitUtils.RemoveInvalidChars(groupName);

    using var collector = new FilteredElementCollector(doc);
    ICollection<Element> groupElements = collector.OfClass(typeof(Group)).ToElements();
    List<Group> groups = groupElements.Cast<Group>().Where(g => g.GroupType.Name == validGroupName).ToList();
    return groups;
  }

  private void CollectSubGroupTypeIds(Document document, Group group, List<ElementId> subGroupTypeIds)
  {
    ICollection<ElementId> groupMemberIds = group.GetMemberIds();

    foreach (ElementId memberId in groupMemberIds)
    {
      Element element = document.GetElement(memberId);

      if (element is Group subgroup)
      {
        subGroupTypeIds.Add(subgroup.GroupType.Id);
        CollectSubGroupTypeIds(document, subgroup, subGroupTypeIds);
      }
    }
  }
}
