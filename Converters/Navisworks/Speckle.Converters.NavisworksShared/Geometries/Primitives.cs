// ---------------------------------------------------------------------------------------------------------------------
// File: Primitives.cs
// Description: Contains definitions for primitives (PointD, TriangleD, LineD) with double-precision vertex storage
//              for use in the Speckle Navisworks converter. These classes ensure higher precision for geometric data
//              compared to the default float-based representations.
//
// ---------------------------------------------------------------------------------------------------------------------
// Notes:
// - These primitives leverage NAV.Vector3D for double-precision vertex representation.
// - Suppression of unused member warnings is intentional to accommodate potential future use cases.
//
// ---------------------------------------------------------------------------------------------------------------------



using System.Diagnostics.CodeAnalysis;

namespace Speckle.Converter.Navisworks.Geometry;

/// <summary>
///   A Point where the vertex is stored with double values as opposed to floats
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class PointD(NAV.Vector3D vertex1)
{
  // ReSharper disable once UnusedAutoPropertyAccessor.Global
  public NAV.Vector3D Vertex1 { get; set; } = vertex1;
}

/// <summary>
///   A Triangle where all vertices are in turn stored with double values as opposed to floats
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class TriangleD(NAV.Vector3D v1, NAV.Vector3D v2, NAV.Vector3D v3)
{
  public NAV.Vector3D Vertex1 { get; set; } = v1;
  public NAV.Vector3D Vertex2 { get; set; } = v2;
  public NAV.Vector3D Vertex3 { get; set; } = v3;
}

/// <summary>
///   A Line where each end point vertex is in turn stored with double values as opposed to floats
/// </summary>
[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class LineD(NAV.Vector3D v1, NAV.Vector3D v2)
{
  public NAV.Vector3D Vertex1 { get; set; } = v1;
  public NAV.Vector3D Vertex2 { get; set; } = v2;
}
