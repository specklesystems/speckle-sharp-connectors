using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Instances;
using Speckle.Sdk.Pipelines.Receive.Artifacts;

namespace Speckle.Connectors.Common.Tests.Instances;

/// <summary>
/// ENG-9110: a block-definition member's object row (which holds its layer and properties) is reached from the
/// geometry / INSTANCE K its definition places, through the graph-native join — DEFINES_MEMBER (25) on the object
/// plane and DEFINES (4) on the geometry plane share the member ordinal; PLACES (24) ties a nested-block member to
/// its INSTANCE node. A member has no DISPLAY edge, so <c>ArtefactRelations.ObjectByGeometry()</c> cannot invert this.
/// </summary>
public class DefinitionMemberIndexesTests
{
  [Test]
  public void Build_JoinsGeometryToMemberByOrdinal()
  {
    var rels = new ArtefactRelations();
    const int DEFINITION = 100;
    // members 7 and 8 at ordinals 0 and 1
    rels.MemberObjectsByDefinition[DEFINITION] = [7, 8];
    rels.MemberOrdByDefinition[DEFINITION] = [0, 1];
    // member 0 owns geometry 40 and 41 (a solid and its display mesh), member 1 owns geometry 42
    rels.DefinesByDefinition[DEFINITION] = [40, 41, 42];
    rels.DefinesOrdByDefinition[DEFINITION] = [0, 0, 1];

    var index = DefinitionMemberIndexes.Build(rels);

    index
      .ObjectByGeometry.Should()
      .Equal(
        new Dictionary<int, int>
        {
          [40] = 7,
          [41] = 7,
          [42] = 8,
        }
      );
    index.ObjectByInstance.Should().BeEmpty();
  }

  [Test]
  public void Build_JoinsNestedInstanceToMemberViaPlaces()
  {
    var rels = new ArtefactRelations();
    rels.MemberObjectsByDefinition[100] = [9];
    rels.MemberOrdByDefinition[100] = [0];
    rels.PlacesByObject[9] = 55; // member object 9 → its INSTANCE node 55

    var index = DefinitionMemberIndexes.Build(rels);

    index.ObjectByInstance.Should().Equal(new Dictionary<int, int> { [55] = 9 });
  }

  [Test]
  public void Build_NoMembers_YieldsEmptyMaps()
  {
    var index = DefinitionMemberIndexes.Build(new ArtefactRelations());

    index.ObjectByGeometry.Should().BeEmpty();
    index.ObjectByInstance.Should().BeEmpty();
  }
}
