using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Speckle.InterfaceGenerator;
using Speckle.Objects.Geometry;
using Speckle.Sdk.Common;
#if NET6_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Speckle.Converters.Common.ToSpeckle;

[GenerateAutoInterface]
public static class MeshInstanceIdGenerator
{
  /// <summary>
  ///
  /// </summary>
  /// <remarks>
  /// There are two implementations of this function because NET Framework lacks some of the Marshall and Span based functions.
  /// However, their external behaviour is the same.
  /// </remarks>
  /// <param name="mesh"></param>
  /// <returns></returns>
  [Pure]
  public static string GenerateUntransformedMeshId(Mesh mesh)
  {
#if NET6_0_OR_GREATER
    return GenerateUntransformedMeshId_Span(mesh.vertices);
#else
    return GenerateUntransformedMeshId(mesh.vertices);
#endif
  }

#if NET6_0_OR_GREATER

  [Pure]
  internal static string GenerateUntransformedMeshId_Span(List<double> vertices)
  {
    ReadOnlySpan<double> span = CollectionsMarshal.AsSpan(vertices);
    ReadOnlySpan<byte> inputBytes = MemoryMarshal.AsBytes(span);

    Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
    SHA256.HashData(inputBytes, hash);

    return Convert.ToHexString(hash);
  }
#endif

  [Pure]
  [SuppressMessage(
    "Performance",
    "CA1850:Prefer static \'HashData\' method over \'ComputeHash\'",
    Justification = "Already another overload for .NET Core"
  )]
  internal static string GenerateUntransformedMeshId(List<double> vertices)
  {
    double[] verts = (double[])s_listItemsField.GetValue(vertices).NotNull();
    int byteCount = verts.Length * sizeof(double);
    byte[] inputBytes = new byte[byteCount];
    Buffer.BlockCopy(verts, 0, inputBytes, 0, byteCount);

    // Compute the SHA256 hash
    using (SHA256 sha256 = SHA256.Create())
    {
      byte[] hashBytes = sha256.ComputeHash(inputBytes);

      // Convert hash to hex string (uppercase, similar to Convert.ToHexString)
      StringBuilder sb = new(hashBytes.Length * 2);
      foreach (byte b in hashBytes)
      {
        sb.AppendFormat("{0:X2}", b);
      }

      return sb.ToString();
    }
  }

  private static readonly FieldInfo s_listItemsField = typeof(List<double>).GetField(
    "_items",
    BindingFlags.NonPublic | BindingFlags.Instance
  )!;
  //TODO: check if a null check would be bad here. if so, include this message
  //do not replace with a null check, I don't want to interrupt the compile time optimisation of reflection
}
