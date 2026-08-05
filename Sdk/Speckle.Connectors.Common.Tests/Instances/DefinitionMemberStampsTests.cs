using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Instances;

namespace Speckle.Connectors.Common.Tests.Instances;

/// <summary>
/// ENG-9110: the eav stamps that join a block-definition member's object row (which holds its layer, via the
/// ordinary object-sourced IN_COLLECTION) back to the geometry / INSTANCE K its definition reaches it by. A member
/// has no DISPLAY edge, so <c>ArtefactRelations.ObjectByGeometry()</c> cannot invert that — these stamps are the
/// only route, and if they don't round-trip the member silently lands on the base layer.
/// </summary>
public class DefinitionMemberStampsTests
{
  private static Dictionary<string, object?> Stamped(params KeyValuePair<string, object?>[] stamps)
  {
    // Mirrors what the eav reader hands back: a dotted path arrives as nested dictionaries, so
    // "@speckle.geometry_k" is ["@speckle"]["geometry_k"].
    var leaves = new Dictionary<string, object?>();
    foreach (var stamp in stamps)
    {
      leaves[stamp.Key[(DefinitionMemberStamps.STAMP_ROOT.Length + 1)..]] = stamp.Value;
    }
    return new Dictionary<string, object?> { [DefinitionMemberStamps.STAMP_ROOT] = leaves };
  }

  [Test]
  public void GeometryStamp_AllKsPointBackAtTheMember()
  {
    // A Rhino member owns several geometry Ks — a lossless 3dm solid AND its display mesh(es) — and receive picks
    // between them per member, so every one of them has to resolve to the same member object.
    var stamp = DefinitionMemberStamps.GeometryStamp(new[] { 7, 8, 9 });

    var index = DefinitionMemberStamps.Read(new Dictionary<int, Dictionary<string, object?>> { [4] = Stamped(stamp) });

    index.ObjectByGeometry.Should().HaveCount(3);
    index.ObjectByGeometry[7].Should().Be(4);
    index.ObjectByGeometry[8].Should().Be(4);
    index.ObjectByGeometry[9].Should().Be(4);
    index.ObjectByInstance.Should().BeEmpty();
  }

  [Test]
  public void GeometryStamp_NoGeometry_EmitsNothing()
  {
    // A member whose every display fragment failed to encode has no K to join to — stamping "" would produce a
    // row that parses to nothing.
    DefinitionMemberStamps.GeometryStamp(Array.Empty<int>()).Should().BeEmpty();
  }

  [Test]
  public void InstanceStamp_JoinsANestedBlockMember()
  {
    var index = DefinitionMemberStamps.Read(
      new Dictionary<int, Dictionary<string, object?>> { [2] = Stamped(DefinitionMemberStamps.InstanceStamp(11)) }
    );

    index.ObjectByInstance[11].Should().Be(2);
    index.ObjectByGeometry.Should().BeEmpty();
  }

  [Test]
  public void Read_AcceptsABareNumber_TheSketchUpForm()
  {
    // SketchUp (ENG-8851) writes a single geometry K as an integer, and eav hands any numeric back as a double.
    // The two connectors share these keys, so a SketchUp bundle must read here unchanged.
    var index = DefinitionMemberStamps.Read(
      new Dictionary<int, Dictionary<string, object?>>
      {
        [3] = new() { [DefinitionMemberStamps.STAMP_ROOT] = new Dictionary<string, object?> { ["geometry_k"] = 5.0 } },
      }
    );

    index.ObjectByGeometry[5].Should().Be(3);
  }

  [Test]
  public void Read_UnstampedBundle_YieldsEmptyMaps()
  {
    // Every top-level object, and every member of a pre-ENG-9110 bundle. Callers fall back to the base layer.
    var index = DefinitionMemberStamps.Read(
      new Dictionary<int, Dictionary<string, object?>>
      {
        [0] = new() { ["name"] = "Wall" },
        [1] = new() { ["@speckle"] = "not a dictionary" },
      }
    );

    index.ObjectByGeometry.Should().BeEmpty();
    index.ObjectByInstance.Should().BeEmpty();
  }

  [Test]
  public void Read_GarbageFragments_AreSkippedNotThrown()
  {
    // A stamp is an optimisation over a lost layer; a malformed one must never fail a whole receive.
    var index = DefinitionMemberStamps.Read(
      new Dictionary<int, Dictionary<string, object?>>
      {
        [6] = new()
        {
          [DefinitionMemberStamps.STAMP_ROOT] = new Dictionary<string, object?>
          {
            ["geometry_k"] = "12, oops, 13",
            ["instance_k"] = null,
          },
        },
      }
    );

    index.ObjectByGeometry.Keys.Should().BeEquivalentTo(new[] { 12, 13 });
    index.ObjectByInstance.Should().BeEmpty();
  }

  [Test]
  public void Read_TwoMembers_KeepTheirOwnOwners()
  {
    var index = DefinitionMemberStamps.Read(
      new Dictionary<int, Dictionary<string, object?>>
      {
        [1] = Stamped(DefinitionMemberStamps.GeometryStamp(new[] { 0 })),
        [2] = Stamped(DefinitionMemberStamps.GeometryStamp(new[] { 1 })),
      }
    );

    // Note the deliberate numeric overlap: geometry K 1 and object K 1 are different things. Keying the map by
    // geometry K is what keeps them apart.
    index.ObjectByGeometry[0].Should().Be(1);
    index.ObjectByGeometry[1].Should().Be(2);
  }
}
