namespace Speckle.Common.StructuralExtrusion;

/// <summary>Indexed triangle mesh: flat xyz vertices and a Speckle face list (<c>3, a, b, c, …</c>).</summary>
public sealed class PrismMesh
{
  public List<double> Vertices { get; }
  public IReadOnlyList<int> Faces { get; }

  public PrismMesh(List<double> vertices, IReadOnlyList<int> faces)
  {
    Vertices = vertices ?? throw new ArgumentNullException(nameof(vertices));
    Faces = faces ?? throw new ArgumentNullException(nameof(faces));
  }
}

/// <summary>
/// A prism of unit length in its local frame: vertices are (x, y, z) with z ∈ {0, 1}, so placing a member is one
/// affine map per vertex and no tessellation (ENG-9048, ADR-0003).
/// </summary>
public sealed class PrismTemplate
{
  public IReadOnlyList<double> LocalVertices { get; }
  public IReadOnlyList<int> Faces { get; }

  internal PrismTemplate(IReadOnlyList<double> localVertices, IReadOnlyList<int> faces)
  {
    LocalVertices = localVertices;
    Faces = faces;
  }
}
