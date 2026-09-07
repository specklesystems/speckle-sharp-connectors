namespace Speckle.Common.SectionGeometry;

/// <summary>
/// A swept section, encoded the way a Speckle mesh expects so connectors can hand it straight over.
/// </summary>
/// <remarks>
/// NOTE: face format is the same as everywhere else in the SDK - a run of [n, i0, i1, ... i(n-1)] groups, where n
/// is that face's vertex count. Side walls come out as quads rather than triangle pairs: fewer indices, cleaner
/// wireframe.
/// </remarks>
public sealed class ProfileMesh
{
  internal ProfileMesh(List<double> vertices, List<int> faces)
  {
    Vertices = vertices;
    Faces = faces;
  }

  /// <summary>Flat x, y, z triples.</summary>
  public List<double> Vertices { get; }

  /// <summary>Vertex-count-prefixed face groups.</summary>
  public List<int> Faces { get; }
}
