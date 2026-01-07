using Speckle.Converter.Navisworks.Paths;

namespace Speckle.Converter.Navisworks.Constants.Registers;

public sealed class InstanceFragmentRegistry : IInstanceFragmentRegistry
{
  private readonly Dictionary<PathKey, PathKey> _pathToGroup = new(PathKey.Comparer);

  private readonly HashSet<PathKey> _converted = new(PathKey.Comparer);

  public bool TryGetGroup(PathKey instancePath, out PathKey groupKey) =>
    _pathToGroup.TryGetValue(instancePath, out groupKey);

  public void RegisterGroup(PathKey groupKey, HashSet<PathKey> instancePaths)
  {
    foreach (var p in instancePaths)
    {
      _pathToGroup[p] = groupKey;
    }
  }

  public void MarkConverted(PathKey instancePath) => _converted.Add(instancePath);

  public IEnumerable<PathKey> GetConvertedPaths() => _converted;

  public Dictionary<PathKey, List<PathKey>> BuildGroupToConvertedPaths()
  {
    var map = new Dictionary<PathKey, List<PathKey>>(PathKey.Comparer);

    foreach (var instancePath in _converted)
    {
      if (!_pathToGroup.TryGetValue(instancePath, out var groupKey))
      {
        continue;
      }

      if (!map.TryGetValue(groupKey, out var list))
      {
        list = [];
        map.Add(groupKey, list);
      }

      list.Add(instancePath);
    }

    return map;
  }
}

public interface IInstanceFragmentRegistry
{
  bool TryGetGroup(PathKey instancePath, out PathKey groupKey);
  void RegisterGroup(PathKey groupKey, HashSet<PathKey> instancePaths);
  void MarkConverted(PathKey instancePath);

  IEnumerable<PathKey> GetConvertedPaths();
  Dictionary<PathKey, List<PathKey>> BuildGroupToConvertedPaths();
}
