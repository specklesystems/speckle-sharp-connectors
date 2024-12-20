using Speckle.DoubleNumerics;

namespace Speckle.Converter.Navisworks.Geometry;

public class Transforms
{
  private readonly double[] _elements;

  /// <summary>
  /// Creates a new 4x4 matrix with the given elements in row-major order.
  /// </summary>
  /// <param name="elements">An array of 16 elements representing the matrix.</param>
  public Transforms(double[] elements)
  {
    if (elements == null || elements.Length != 16)
    {
      throw new ArgumentException("A 4x4 matrix must have exactly 16 elements.", nameof(elements));
    }

    this._elements = elements;
  }

  /// <summary>
  /// Accesses the element at the given row and column.
  /// </summary>
  public double this[int row, int col]
  {
    get => _elements[row * 4 + col];
    set => _elements[row * 4 + col] = value;
  }

  /// <summary>
  /// Applies the transformation defined by this matrix to a 3D vector.
  /// </summary>
  public NAV.Vector3D Transform(Vector3 vector)
  {
    var t1 = this[0, 3] * vector.X + this[1, 3] * vector.Y + this[2, 3] * vector.Z + this[3, 3];
    if (t1 == 0)
    {
      t1 = 1; // Prevent division by zero
    }

    var x = (this[0, 0] * vector.X + this[0, 1] * vector.Y + this[0, 2] * vector.Z + this[0, 3]) / t1;
    var y = (this[1, 0] * vector.X + this[1, 1] * vector.Y + this[1, 2] * vector.Z + this[1, 3]) / t1;
    var z = (this[2, 0] * vector.X + this[2, 1] * vector.Y + this[2, 2] * vector.Z + this[2, 3]) / t1;

    return new NAV.Vector3D(x, y, z);
  }

  /// <summary>
  /// Creates an identity matrix.
  /// </summary>
  public static Transforms Identity() => new([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]);

  /// <summary>
  /// Returns the matrix as a flat array in row-major order.
  /// </summary>
  public double[] ToArray() => (double[])_elements.Clone();
}
