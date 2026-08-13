namespace Speckle.Connectors.Common.Topology;

/// <summary>The element→element relation kinds <see cref="PlacementTopology.Resolve"/> can produce.</summary>
public enum PlacementEdgeKind
{
  /// <summary>Owner → owned component (a nested shared family → its super-component). Ownership, not placement.</summary>
  Subelement,

  /// <summary>Hosted element → its host (a door → its wall). Placement, not ownership.</summary>
  HostedOn,

  /// <summary>Element → the ROOM object containing it.</summary>
  InRoom,

  /// <summary>Room → adjacent room, scoped by the opening between them.</summary>
  ConnectsTo,
}

/// <summary>
/// What one sent element reports about its neighbours, as plain ids — the host-API read (Revit
/// <c>SuperComponent</c> / <c>Host</c> / <c>Room</c> / <c>FromRoom</c> / <c>ToRoom</c>) already done and the host
/// types left behind.
/// </summary>
/// <param name="ObjectK">The K this element was interned as, in this placement.</param>
/// <param name="OwnerUniqueId">Its super-component, if any. Non-null SUPPRESSES hosting even when unresolvable —
/// see <see cref="PlacementTopology.Resolve"/>.</param>
/// <param name="HostUniqueId">What it is placed on, if anything.</param>
/// <param name="RoomUniqueId">The room (or MEP space) containing it, if any.</param>
/// <param name="FromRoomUniqueId">For an opening: the room on one side.</param>
/// <param name="ToRoomUniqueId">For an opening: the room on the other side.</param>
public readonly record struct PlacementElement(
  string UniqueId,
  int ObjectK,
  string? OwnerUniqueId,
  string? HostUniqueId,
  string? RoomUniqueId,
  string? FromRoomUniqueId,
  string? ToRoomUniqueId
);

/// <summary>One resolved edge, in interned Ks.</summary>
/// <param name="Ord">Ordinal for most kinds; for <see cref="PlacementEdgeKind.ConnectsTo"/> it is a SCOPE (the
/// opening's object K), matching <c>rel_types.ord_semantics='scope'</c>.</param>
public readonly record struct PlacementEdge(PlacementEdgeKind Kind, int Src, int Dst, int Ord);

/// <summary>
/// Resolves element→element topology WITHIN ONE PLACEMENT of a source document [ENG-9212].
///
/// <para><b>Why this exists as its own boundary.</b> When the same file is placed more than once — a Revit link
/// placed twice, one linked apartment per floor — every occurrence repeats the same source element ids. Identity is
/// therefore <i>(placement, sourceId)</i>, never sourceId alone, and a resolver keyed on sourceId alone gets it
/// wrong in two directions at once: a sourceId that maps to N candidate Ks either fans out over all of them
/// (placement A's door reported as hosted on placement B's wall — a cartesian product of edges, N² for N placements)
/// or picks the first (every placement's furniture reported in the FIRST placement's room). Both produce a bundle
/// whose geometry renders correctly while its hierarchy and relations are quietly wrong.</para>
///
/// <para>The fix is a per-placement index, which makes every endpoint unambiguous — exactly one K per source id, no
/// list to fan out over and no <c>[0]</c> to guess with. That resolution is what this class holds, free of any host
/// API, so it can be tested against duplicate source ids across placements; the caller does the host-API read and
/// turns the returned edges into pipeline calls. A host document, or a file placed exactly once, is just the
/// one-placement case — the index is then what a global one would have been.</para>
///
/// <para><b>Not for value nodes.</b> Levels, materials, definitions and the like are SHARED across placements, so
/// their edges legitimately fan out to every occurrence. Those resolve through a flattened index instead; only
/// element→element topology belongs here.</para>
/// </summary>
public static class PlacementTopology
{
  /// <summary>
  /// Resolves <paramref name="elements"/> against <paramref name="objectKsByUniqueId"/> — the source id → object K
  /// index for THIS placement, and only this placement.
  ///
  /// <para><b>Ownership wins over hosting</b>, matching rvextract's precedence (<c>owningElemId</c> first,
  /// <c>getHostId</c> as the fallback) so the same fixture gets the same relation whether it is published through a
  /// connector or extracted from an uploaded file. An owner that exists but was NOT sent suppresses
  /// <see cref="PlacementEdgeKind.HostedOn"/> rather than falling through to it: the element IS owned, and the edge
  /// is simply dropped as dangling.</para>
  ///
  /// <para>Every other endpoint must also be present in the index — an edge to something that was not sent would
  /// dangle, so it is dropped. Order is the input order, then ownership/hosting → room → adjacency per element.</para>
  /// </summary>
  public static IReadOnlyList<PlacementEdge> Resolve(
    IReadOnlyCollection<PlacementElement> elements,
    IReadOnlyDictionary<string, int> objectKsByUniqueId
  )
  {
    var edges = new List<PlacementEdge>();
    foreach (var element in elements)
    {
      int elementK = element.ObjectK;

      if (element.OwnerUniqueId is { } ownerUniqueId)
      {
        if (objectKsByUniqueId.TryGetValue(ownerUniqueId, out int ownerK))
        {
          edges.Add(new PlacementEdge(PlacementEdgeKind.Subelement, ownerK, elementK, 0));
        }
      }
      else if (element.HostUniqueId is { } hostUniqueId && objectKsByUniqueId.TryGetValue(hostUniqueId, out int hostK))
      {
        edges.Add(new PlacementEdge(PlacementEdgeKind.HostedOn, elementK, hostK, 0));
      }

      if (element.RoomUniqueId is { } roomUniqueId && objectKsByUniqueId.TryGetValue(roomUniqueId, out int roomK))
      {
        edges.Add(new PlacementEdge(PlacementEdgeKind.InRoom, elementK, roomK, 0));
      }

      // The opening's own K is the SCOPE of the adjacency, so all three endpoints come from this placement.
      if (
        element.FromRoomUniqueId is { } fromRoomUniqueId
        && element.ToRoomUniqueId is { } toRoomUniqueId
        && objectKsByUniqueId.TryGetValue(fromRoomUniqueId, out int fromK)
        && objectKsByUniqueId.TryGetValue(toRoomUniqueId, out int toK)
      )
      {
        edges.Add(new PlacementEdge(PlacementEdgeKind.ConnectsTo, fromK, toK, elementK));
      }
    }

    return edges;
  }
}
