using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.HostApp;

/// <summary>
/// Owns how a receive marks its own output in the document and finds it again next time [ENG-8805]: it bakes the
/// top-level Group everything lands in, records that Group and the Materials the receive created in the
/// <see cref="RevitReceiveManifest"/>, and — at the start of the next receive — deletes exactly those.
/// </summary>
/// <remarks>
/// <para>Grouping and tracking are one concern, not two: the Group <i>is</i> the handle on a received model, for the
/// user (select/move/delete it in one click) and for the next receive alike. Both receive paths — the v1
/// <c>RevitHostObjectBuilder</c> and the artefact <c>RevitHostObjectArtefactBuilder</c> — go through here, so a
/// document that has been received into by either is cleaned up correctly by the other.</para>
/// <para>Cleanup runs newest tracking mechanism first, because a document can hold a bake from any generation of this
/// connector: the manifest's Group by UniqueId (rename-proof), then the manifest's Materials, then a name-based Group
/// purge for bakes that predate the manifest, then the transitional Comments-marker sweep for the generation of the
/// artefact builder that tracked elements by stamping that parameter.</para>
/// <para>Callers must have an open transaction; every method mutates the document.</para>
/// </remarks>
public class RevitReceiveTracker
{
  private readonly RevitReceiveManifest _manifest;
  private readonly RevitGroupBaker _groupBaker;
  private readonly RevitMaterialBaker _materialBaker;
  private readonly RevitViewBaker _viewBaker;

  public RevitReceiveTracker(
    RevitReceiveManifest manifest,
    RevitGroupBaker groupBaker,
    RevitMaterialBaker materialBaker,
    RevitViewBaker viewBaker
  )
  {
    _manifest = manifest;
    _groupBaker = groupBaker;
    _materialBaker = materialBaker;
    _viewBaker = viewBaker;
  }

  // The record this receive found on the way in, so BakeGroupAndRecord updates that DataStorage in place rather than
  // leaving one behind per receive. Scoped per receive, like the rest of the bakers.
  private RevitReceiveRecord? _prior;

  /// <summary>Deletes what the previous receive of this model left in <paramref name="doc"/>. See the class remarks
  /// for the order and why there are four mechanisms.</summary>
  public void PurgePriorReceive(Document doc, string marker, string? projectId, string? modelId)
  {
    _prior = _manifest.Find(doc, projectId, modelId, marker);

    if (_prior?.GroupUniqueId is { Length: > 0 } priorGroup)
    {
      _groupBaker.PurgeGroupById(priorGroup);
    }

    // Groups first, so these materials are no longer painted onto anything this receive is replacing.
    _materialBaker.PurgeMaterials(_prior?.MaterialUniqueIds ?? []);

    // Bakes that predate the manifest — including v1 bakes, and the artefact builder's pre-ENG-9101 delegation to it.
    _groupBaker.PurgeGroups(marker);
    // Same marker convention, but a View3D can be neither grouped nor swept by PurgeMarkedElements below.
    _viewBaker.PurgeArtefactViews(marker);
  }

  /// <summary>
  /// Transitional: deletes elements stamped with the Comments marker by the generation of the artefact builder that
  /// tracked them that way, which left no Group behind for <see cref="PurgePriorReceive"/> to find.
  /// </summary>
  /// <remarks>DirectShape (atomic objects and DirectShape instances) and FamilyInstance (instances baked as families)
  /// were both stamped, so sweeping both covers whichever setting that receive ran with. Removable once no document
  /// received into by those builds is still in circulation.</remarks>
  public static void PurgeMarkedElements(Document doc, string marker)
  {
    var toDelete = new List<ElementId>();
    CollectMarked(doc, typeof(DirectShape), marker, toDelete);
    CollectMarked(doc, typeof(FamilyInstance), marker, toDelete);
    if (toDelete.Count > 0)
    {
      doc.Delete(toDelete);
    }
  }

  /// <summary>
  /// Bakes <paramref name="groupMembers"/> into the pinned top-level group and records it, together with the
  /// materials this receive created, for the next receive to clean up. Returns the group's UniqueId, or null when
  /// there was nothing to group.
  /// </summary>
  /// <param name="groupMembers">The elements to group, or null to use whatever was accumulated through
  /// <see cref="RevitGroupBaker.AddToTopLevelGroup"/> — the v1 builder collects members that way.</param>
  public string? BakeGroupAndRecord(
    Document doc,
    string marker,
    string? projectId,
    string? modelId,
    IReadOnlyCollection<ElementId>? groupMembers,
    IReadOnlyCollection<string> createdMaterialUniqueIds
  )
  {
    var groupUniqueId = groupMembers is null
      ? _groupBaker.BakeGroupForTopLevel(marker)
      : _groupBaker.BakeGroupForTopLevel(marker, groupMembers);

    _manifest.Write(doc, _prior?.Storage, projectId, modelId, marker, groupUniqueId, createdMaterialUniqueIds);
    return groupUniqueId;
  }

  /// <summary>Records this receive without baking a group — for when grouping failed, so the manifest still carries
  /// the materials to clean up next time.</summary>
  public void Record(
    Document doc,
    string marker,
    string? projectId,
    string? modelId,
    IReadOnlyCollection<string> createdMaterialUniqueIds
  ) => _manifest.Write(doc, _prior?.Storage, projectId, modelId, marker, null, createdMaterialUniqueIds);

  private static void CollectMarked(Document doc, Type elementClass, string marker, List<ElementId> toDelete)
  {
    using var collector = new FilteredElementCollector(doc);
    foreach (var element in collector.OfClass(elementClass))
    {
      if (
        element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.AsString() is string c
        && string.Equals(c, marker, StringComparison.Ordinal)
      )
      {
        toDelete.Add(element.Id);
      }
    }
  }
}
