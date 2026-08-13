using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Topology;

namespace Speckle.Connectors.Common.Tests.Topology;

/// <summary>
/// ENG-9212: when the same file is placed more than once (a Revit link placed twice, one linked apartment per
/// floor) every occurrence repeats the same source element ids, so identity is (placement, sourceId). Resolving on
/// sourceId alone produced a cartesian product of hosting/ownership edges across placements and pinned every room
/// relation to the first placement — a bundle that renders correctly while its relations point at the wrong
/// occurrence. These tests pin the placement scoping, which no Revit-document test could reach.
/// </summary>
public class PlacementTopologyTests
{
  // The same two source elements — a door and the wall it is hosted on — as they appear in BOTH placements of one
  // linked file. Identical UniqueIds; only the interned Ks differ.
  private const string DOOR = "door-uid";
  private const string WALL = "wall-uid";
  private const string ROOM = "room-uid";
  private const string ROOM_B = "room-b-uid";

  private static PlacementElement Hosted(string uniqueId, int objectK, string hostUniqueId) =>
    new(uniqueId, objectK, null, hostUniqueId, null, null, null);

  private static Dictionary<string, int> Index(params (string UniqueId, int ObjectK)[] entries)
  {
    var index = new Dictionary<string, int>(StringComparer.Ordinal);
    foreach (var (uniqueId, objectK) in entries)
    {
      index[uniqueId] = objectK;
    }
    return index;
  }

  [Test]
  public void TwoPlacements_HostingStaysInsideItsOwnPlacement()
  {
    // Placement A interned door→1 / wall→2, placement B the same elements as 11 / 12. Resolved per placement, each
    // door reaches its OWN wall: 2 edges total. A global sourceId → K-list index gave 4 (both cross pairs included).
    var a = PlacementTopology.Resolve([Hosted(DOOR, 1, WALL)], Index((DOOR, 1), (WALL, 2)));
    var b = PlacementTopology.Resolve([Hosted(DOOR, 11, WALL)], Index((DOOR, 11), (WALL, 12)));

    a.Should().ContainSingle().Which.Should().Be(new PlacementEdge(PlacementEdgeKind.HostedOn, 1, 2, 0));
    b.Should().ContainSingle().Which.Should().Be(new PlacementEdge(PlacementEdgeKind.HostedOn, 11, 12, 0));

    // And explicitly: nothing crosses. No edge pairs a K from one placement with a K from the other.
    a.Concat(b)
      .Should()
      .OnlyContain(e => (e.Src < 10 && e.Dst < 10) || (e.Src >= 10 && e.Dst >= 10), "no edge may cross placements");
  }

  [Test]
  public void TwoPlacements_RoomResolvesToTheMatchingOccurrenceNotTheFirst()
  {
    // The regression this replaces indexed [0] — so a fixture in placement B was reported inside placement A's room.
    var fixtureInB = new PlacementElement("fixture-uid", 11, null, null, ROOM, null, null);

    var edges = PlacementTopology.Resolve([fixtureInB], Index(("fixture-uid", 11), (ROOM, 12)));

    edges.Should().ContainSingle().Which.Should().Be(new PlacementEdge(PlacementEdgeKind.InRoom, 11, 12, 0));
  }

  [Test]
  public void TwoPlacements_AdjacencyKeepsAllThreeEndpointsInOnePlacement()
  {
    // CONNECTS_TO carries the opening's K as its SCOPE, so the scope is a third endpoint that must not cross either.
    var door = new PlacementElement(DOOR, 11, null, null, null, ROOM, ROOM_B);

    var edges = PlacementTopology.Resolve([door], Index((DOOR, 11), (ROOM, 12), (ROOM_B, 13)));

    edges.Should().ContainSingle().Which.Should().Be(new PlacementEdge(PlacementEdgeKind.ConnectsTo, 12, 13, 11));
  }

  [Test]
  public void OwnershipWinsOverHosting()
  {
    // rvextract's precedence (owningElemId before getHostId) so a connector publish and a file-upload extract of the
    // same model agree. Note the argument order flips: Subelement is owner→child, HostedOn is child→host.
    var ownedAndHosted = new PlacementElement("fixture-uid", 1, "owner-uid", WALL, null, null, null);

    var edges = PlacementTopology.Resolve([ownedAndHosted], Index(("fixture-uid", 1), ("owner-uid", 2), (WALL, 3)));

    edges.Should().ContainSingle().Which.Should().Be(new PlacementEdge(PlacementEdgeKind.Subelement, 2, 1, 0));
  }

  [Test]
  public void UnsentOwner_SuppressesHostingRatherThanFallingThroughToIt()
  {
    // The element IS owned; that its owner was filtered out of the send does not make it hosted. The ownership edge
    // is dropped as dangling and no HOSTED_ON takes its place.
    var owned = new PlacementElement("fixture-uid", 1, "owner-not-sent", WALL, null, null, null);

    var edges = PlacementTopology.Resolve([owned], Index(("fixture-uid", 1), (WALL, 3)));

    edges.Should().BeEmpty();
  }

  [Test]
  public void UnsentEndpoints_AreDroppedNotDangled()
  {
    // A publish filter that excludes walls and rooms must not leave edges pointing at Ks that are not in the bundle.
    var door = new PlacementElement(DOOR, 1, null, WALL, ROOM, ROOM, ROOM_B);

    var edges = PlacementTopology.Resolve([door], Index((DOOR, 1)));

    edges.Should().BeEmpty();
  }

  [Test]
  public void SinglePlacement_BehavesExactlyLikeTheGlobalIndexDid()
  {
    // Host-document elements and single-placement links are the one-placement case: the per-placement index is
    // precisely what the global one used to be, so their edges are unchanged by the ENG-9212 rework.
    var door = new PlacementElement(DOOR, 1, null, WALL, ROOM, ROOM, ROOM_B);

    var edges = PlacementTopology.Resolve([door], Index((DOOR, 1), (WALL, 2), (ROOM, 3), (ROOM_B, 4)));

    edges
      .Should()
      .Equal(
        new PlacementEdge(PlacementEdgeKind.HostedOn, 1, 2, 0),
        new PlacementEdge(PlacementEdgeKind.InRoom, 1, 3, 0),
        new PlacementEdge(PlacementEdgeKind.ConnectsTo, 3, 4, 1)
      );
  }

  [Test]
  public void RepeatedPlacements_EmitOneEdgeEachRatherThanNSquared()
  {
    // Four placements of one linked file: 4 HOSTED_ON edges, not 16. relations.parquet is an append-only log with no
    // dedup, so a count regression here ships as duplicate rows rather than an error.
    var edges = Enumerable
      .Range(0, 4)
      .SelectMany(placement =>
      {
        int doorK = placement * 10;
        int wallK = doorK + 1;
        return PlacementTopology.Resolve([Hosted(DOOR, doorK, WALL)], Index((DOOR, doorK), (WALL, wallK)));
      })
      .ToList();

    edges.Should().HaveCount(4);
    edges.Should().OnlyContain(e => e.Dst == e.Src + 1);
  }
}
