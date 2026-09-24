using Grasshopper.Kernel.Types;

namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The object→object relations Grasshopper can author [ENG-9475]. Values are the bundle-spec <c>rel_types.rel</c> ids,
/// so a saved definition keeps meaning if the order here ever changes. The spec vocabulary is closed - a new relation
/// is a spec change first, then a row here and a case in the artefact builder's <c>EmitRelations</c>.
/// </summary>
/// <remarks>
/// Only relations whose BOTH ends are objects belong here. IN_ROOM / BOUNDS want a room object Grasshopper cannot make,
/// and IN_GROUP / IN_SYSTEM point at container nodes, which is a different component if it is ever wanted.
/// </remarks>
public enum SpeckleRelationType
{
  /// <summary>Analyzer-mandated zero. Never authored; readers treat it as "not a relation".</summary>
  None = 0,
  Subelement = 3,
  InAssembly = 18,
  ConnectsTo = 21,
  HostedOn = 22,
}

/// <summary>How one relation type reads on the canvas: its two ends, named in the spec's direction.</summary>
public sealed record SpeckleRelationTypeInfo(
  SpeckleRelationType Type,
  string SpecName,
  string SourceName,
  string SourceNickName,
  string SourceDescription,
  string TargetName,
  string TargetNickName,
  string TargetDescription,
  string Description
)
{
  /// <summary>The spec name the way Explore shows it: <c>HOSTED_ON</c> → <c>Hosted On</c>.</summary>
  public string Label { get; } = Humanise(SpecName);

  /// <summary>Mirrors Explore's humaniser, so a relation reads the same when authored and when loaded.</summary>
  private static string Humanise(string specName) =>
    string.Join(
      " ",
      specName
        .Split('_')
        .Where(part => part.Length > 0)
        .Select(part => char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant())
    );
}

public static class SpeckleRelationTypes
{
  /// <summary>Menu order.</summary>
  public static readonly IReadOnlyList<SpeckleRelationTypeInfo> All =
  [
    new(
      SpeckleRelationType.HostedOn,
      "HOSTED_ON",
      "Hosted",
      "H",
      "The element placed on the host, e.g. a door.",
      "Host",
      "Ho",
      "What the element is placed on, e.g. a wall.",
      "Placement, not ownership: the hosted element sits on its host but is not a component of it. One host per element."
    ),
    new(
      SpeckleRelationType.ConnectsTo,
      "CONNECTS_TO",
      "From",
      "F",
      "The object the connection starts at.",
      "To",
      "T",
      "The object the connection ends at.",
      "Directed connectivity between two objects, e.g. a connector to the column, beam and slab it joins."
    ),
    new(
      SpeckleRelationType.InAssembly,
      "IN_ASSEMBLY",
      "Member",
      "M",
      "An object that belongs to the assembly.",
      "Assembly",
      "A",
      "The assembly object the member belongs to.",
      "Fabrication membership. The first member published for an assembly is its main member."
    ),
    new(
      SpeckleRelationType.Subelement,
      "SUBELEMENT",
      "Parent",
      "P",
      "The owning object, e.g. a curtain wall.",
      "Child",
      "C",
      "A component of the parent, e.g. a mullion.",
      "Ownership: the child is a component of the parent. One parent per child."
    ),
  ];

  /// <summary>Whether a stored int is a relation this build can write. Rejects <see cref="SpeckleRelationType.None"/>.</summary>
  public static bool IsAuthorable(int value) => All.Any(i => (int)i.Type == value);

  public static SpeckleRelationTypeInfo Info(SpeckleRelationType type) =>
    All.FirstOrDefault(i => i.Type == type)
    ?? throw new ArgumentOutOfRangeException(nameof(type), type, "Relation type is not authorable from Grasshopper");
}

/// <summary>
/// One typed edge between two objects, held by application id. Objects stay independent on the canvas: the relation is
/// its own goo, wired into Publish next to the objects it names, and resolved to object Ks only at publish time.
/// </summary>
/// <remarks>
/// Names are a snapshot for the canvas tooltip, nothing more. Holding the end wrappers would go stale on every deep
/// copy and bloat internalised data.
/// </remarks>
public sealed class SpeckleRelation
{
  /// <summary><see cref="Speckle.Connectors.Common.Conversion.ConversionResult.SourceType"/> of a relation the
  /// artefact builder could not publish - how Publish tells them apart from object results.</summary>
  public const string SOURCE_TYPE = "Relation";

  public required SpeckleRelationType Type { get; init; }
  public required string SourceId { get; init; }
  public required string TargetId { get; init; }
  public string? SourceName { get; init; }
  public string? TargetName { get; init; }

  public SpeckleRelationTypeInfo Info => SpeckleRelationTypes.Info(Type);

  public bool SameEdgeAs(SpeckleRelation other) =>
    Type == other.Type
    && string.Equals(SourceId, other.SourceId, StringComparison.Ordinal)
    && string.Equals(TargetId, other.TargetId, StringComparison.Ordinal);

  public override string ToString() =>
    $"{Info.Label}: {Describe(SourceName, SourceId)} → {Describe(TargetName, TargetId)}";

  private static string Describe(string? name, string id) => string.IsNullOrWhiteSpace(name) ? id : name!;

  /// <summary>
  /// Splits relation goos out of a Publish input list. Relations are model-scoped edges, not scene-tree members, so
  /// they ride the root collection wrapper (next to model properties) rather than the collection tree - and taking
  /// them out first keeps Publish's single-collection fast path intact.
  /// </summary>
  /// <returns>The goos that are not relations, order and null placeholders preserved.</returns>
  public static List<IGH_Goo> Peel(IEnumerable<IGH_Goo> goos, List<SpeckleRelation> relations)
  {
    var rest = new List<IGH_Goo>();
    foreach (var goo in goos)
    {
      if (goo is SpeckleRelationGoo { Value: { } relation })
      {
        relations.Add(relation);
      }
      else
      {
        rest.Add(goo);
      }
    }
    return rest;
  }
}
