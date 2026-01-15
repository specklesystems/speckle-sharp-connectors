using System.Diagnostics.CodeAnalysis;

namespace Speckle.Converter.Navisworks.Geometry;

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public readonly struct SafeBoundingBox
{
  public SafeVertex Center { get; }
  public SafeVertex Min { get; }
  public SafeVertex Max { get; }
  public double SizeX { get; }
  public double SizeY { get; }
  public double SizeZ { get; }

  public SafeBoundingBox(NAV.BoundingBox3D boundingBox)
  {
    if (boundingBox == null)
    {
      throw new ArgumentNullException(nameof(boundingBox));
    }

    Center = new SafeVertex(boundingBox.Center);
    Min = new SafeVertex(boundingBox.Min);
    Max = new SafeVertex(boundingBox.Max);
    SizeX = boundingBox.Size.X;
    SizeY = boundingBox.Size.Y;
    SizeZ = boundingBox.Size.Z;
  }
}

/// <summary>
/// Safe structure to store vector coordinates without COM dependency
/// </summary>
public readonly struct SafeVector
{
  public double X { get; }
  public double Y { get; }
  public double Z { get; }

  public SafeVector(NAV.Vector3D vector)
  {
    if (vector == null)
    {
      throw new ArgumentNullException(nameof(vector));
    }
    X = vector.X;
    Y = vector.Y;
    Z = vector.Z;
  }

  public SafeVector(NAV.Point3D point)
  {
    if (point == null)
    {
      throw new ArgumentNullException(nameof(point));
    }
    X = point.X;
    Y = point.Y;
    Z = point.Z;
  }

  // Constructor for raw coordinates
  public SafeVector(double x, double y, double z)
  {
    X = x;
    Y = y;
    Z = z;
  }
}

public readonly struct SafeVertex
{
  public double X { get; }
  public double Y { get; }
  public double Z { get; }

  public SafeVertex(NAV.Vector3D vector)
  {
    if (vector == null)
    {
      throw new ArgumentNullException(nameof(vector));
    }

    X = vector.X;
    Y = vector.Y;
    Z = vector.Z;
  }

  public SafeVertex(NAV.Point3D point)
  {
    if (point == null)
    {
      throw new ArgumentNullException(nameof(point));
    }
    X = point.X;
    Y = point.Y;
    Z = point.Z;
  }

  // Constructor for raw coordinates
  public SafeVertex(double x, double y, double z)
  {
    X = x;
    Y = y;
    Z = z;
  }
}

[SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
public readonly struct SafePoint
{
  public SafeVertex Vertex { get; }

  public SafePoint(NAV.Vector3D point)
  {
    if (point == null)
    {
      throw new ArgumentNullException(nameof(point));
    }

    Vertex = new SafeVertex(point);
  }
}

public readonly struct SafeTriangle
{
  public SafeVertex Vertex1 { get; }
  public SafeVertex Vertex2 { get; }
  public SafeVertex Vertex3 { get; }

  public SafeTriangle(NAV.Vector3D v1, NAV.Vector3D v2, NAV.Vector3D v3)
  {
    if (v1 == null || v2 == null || v3 == null)
    {
      throw new ArgumentNullException(nameof(v1));
    }

    Vertex1 = new SafeVertex(v1);
    Vertex2 = new SafeVertex(v2);
    Vertex3 = new SafeVertex(v3);
  }
}

public readonly struct SafeLine
{
  public SafeVertex Start { get; }
  public SafeVertex End { get; }

  public SafeLine(NAV.Vector3D start, NAV.Vector3D end)
  {
    if (start == null || end == null)
    {
      throw new ArgumentNullException(nameof(start));
    }

    Start = new SafeVertex(start);
    End = new SafeVertex(end);
  }
}
