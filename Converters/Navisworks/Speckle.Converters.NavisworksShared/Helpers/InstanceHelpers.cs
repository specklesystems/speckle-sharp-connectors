using Autodesk.Navisworks.Api.Interop.ComApi;
using Speckle.Converter.Navisworks.Paths;

namespace Speckle.Converter.Navisworks.Helpers;

internal static class InstanceHelpers
{
  private static HashSet<PathKey> DiscoverInstancePaths(InwOaPath path)
  {
    var set = new HashSet<PathKey>(PathKey.Comparer);

    foreach (InwOaFragment3 fragment in path.Fragments().OfType<InwOaFragment3>())
    {
      if (fragment.path?.ArrayData is not Array arr)
      {
        continue;
      }

      set.Add(PathKey.FromComArray(arr));
    }

    return set;
  }
}
