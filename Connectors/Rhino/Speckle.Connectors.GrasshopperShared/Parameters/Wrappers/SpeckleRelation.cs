namespace Speckle.Connectors.GrasshopperShared.Parameters;

/// <summary>
/// The object→object relations Grasshopper can author [ENG-9475]. Values are the bundle-spec <c>rel_types.rel</c> ids,
/// so a saved definition keeps meaning if the order here ever changes. The spec vocabulary is closed - a new relation
/// is a spec change first, then a row in <see cref="SpeckleRelationTypes.All"/> and a case in the artefact builder's
/// <c>EmitRelations</c>.
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
/// <remarks>Nicknames are the full words on purpose - direction is the whole point of the labels.</remarks>
public sealed record SpeckleRelationTypeInfo(
  SpeckleRelationType Type,
  string SpecName,
  string SourceName,
  string SourceDescription,
  string TargetName,
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
      "The element placed on the host, e.g. a door. Comes out again as Related.",
      "Host",
      "What the element is placed on, e.g. a wall.",
      "Placement, not ownership: the hosted element sits on its host but is not a component of it. One host per element."
    ),
    new(
      SpeckleRelationType.ConnectsTo,
      "CONNECTS_TO",
      "From",
      "The object the connections start at. Comes out again as Related.",
      "To",
      "The objects the connections end at.",
      "Directed connectivity between objects, e.g. a connector to the column, beam and slab it joins."
    ),
    new(
      SpeckleRelationType.InAssembly,
      "IN_ASSEMBLY",
      "Member",
      "An object that belongs to the assembly. Comes out again as Related.",
      "Assembly",
      "The assembly object the member belongs to.",
      "Fabrication membership. The first member published for an assembly is its main member."
    ),
    new(
      SpeckleRelationType.Subelement,
      "SUBELEMENT",
      "Parent",
      "The owning object, e.g. a curtain wall. Comes out again as Related.",
      "Child",
      "Components of the parent, e.g. its mullions.",
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
/// One outgoing edge of the object that carries it (<see cref="SpeckleWrapper.Relations"/>), pointing at its target by
/// application id - the same shape the bundle stores: src is the carrier, dst is this. The Speckle Relation component
/// is a passthrough: the source object comes out with its relations attached and is what gets published.
/// </summary>
/// <remarks>
/// The target name is a snapshot for the canvas tooltip, nothing more. Holding the target wrapper would go stale on
/// every deep copy.
/// </remarks>
public sealed class SpeckleRelation
{
  /// <summary><see cref="Speckle.Connectors.Common.Conversion.ConversionResult.SourceType"/> of a relation the
  /// artefact builder could not publish - how Publish tells them apart from object results.</summary>
  public const string SOURCE_TYPE = "Relation";

  public required SpeckleRelationType Type { get; init; }
  public required string TargetId { get; init; }
  public string? TargetName { get; init; }

  public SpeckleRelationTypeInfo Info => SpeckleRelationTypes.Info(Type);

  public bool SameEdgeAs(SpeckleRelation other) =>
    Type == other.Type && string.Equals(TargetId, other.TargetId, StringComparison.Ordinal);

  public override string ToString() =>
    $"{Info.Label} → {(string.IsNullOrWhiteSpace(TargetName) ? TargetId : TargetName)}";
}
