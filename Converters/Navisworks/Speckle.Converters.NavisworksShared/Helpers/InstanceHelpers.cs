using System.Runtime.InteropServices;
using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Paths;

namespace Speckle.Converter.Navisworks.Helpers;

internal static class InstanceHelpers
{
  private static HashSet<PathKey> DiscoverInstancePaths(InwOaPath path)
  {
    var set = new HashSet<PathKey>(PathKey.Comparer);
    var fragments = path.Fragments();

    try
    {
      foreach (InwOaFragment3 fragment in fragments.OfType<InwOaFragment3>())
      {
        GC.KeepAlive(fragment);

        var fragPath = fragment.path;
        if (fragPath?.ArrayData is not Array arr)
        {
          continue;
        }

        set.Add(PathKey.FromComArray(arr));

        if (fragPath != null)
        {
          Marshal.ReleaseComObject(fragPath);
        }
      }
    }
    finally
    {
      if (fragments != null)
      {
        Marshal.ReleaseComObject(fragments);
      }
    }

    return set;
  }
}
