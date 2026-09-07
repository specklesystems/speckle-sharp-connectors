namespace Speckle.Common.SectionGeometry;

/// <summary>
/// How faithfully a <see cref="SectionProfile"/> represents the host's section.
/// </summary>
/// <remarks>
/// We will never draw all ~47 CSi shapes exactly. Rather than guess silently, resolvers report which tier they
/// landed on so the connector can publish it against the section.
/// </remarks>
public enum ProfileFidelity
{
  /// <summary>The section's true outline, built from its named dimensions.</summary>
  Exact,

  /// <summary>No builder for the shape, but t3/t2 were known. Right size, wrong shape.</summary>
  BoundingEnvelope,

  /// <summary>
  /// Nothing resolved, not even overall dimensions. No profile is produced - the caller falls back to a line
  /// (frame) or a plane (shell).
  /// </summary>
  /// <remarks>No profile ever carries this. It exists so callers holding a null have a name and a label for it.</remarks>
  NotExtruded,
}

/// <summary>Labels for <see cref="ProfileFidelity"/>, so every connector publishes the same wording.</summary>
public static class ProfileFidelityExtensions
{
  /// <summary>The label to publish for this fidelity.</summary>
  public static string ToLabel(this ProfileFidelity fidelity) =>
    fidelity switch
    {
      ProfileFidelity.Exact => "Exact",
      ProfileFidelity.BoundingEnvelope => "Bounding envelope",
      ProfileFidelity.NotExtruded => "Not extruded",
      _ => "Unknown",
    };
}
