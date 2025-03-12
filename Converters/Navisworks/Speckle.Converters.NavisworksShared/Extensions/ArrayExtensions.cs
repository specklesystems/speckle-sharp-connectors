using System.Runtime.CompilerServices;

namespace Speckle.Converter.Navisworks.Extensions;

internal static class ArrayExtension
{
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  public static T[] ToArray<T>(this Array arr)
    where T : struct
  {
    var result = new T[arr.Length];
    Array.Copy(arr, result, result.Length);
    return result;
  }
}
