using Speckle.Converter.Navisworks.Helpers;
using Speckle.Converter.Navisworks.Paths;

namespace Speckle.Converter.Navisworks.Constants.Registers;

public interface IInstanceFragmentRegistry
{
  bool TryGetGroup(PathKey instancePath, out PathKey groupKey);
  void RegisterGroup(PathKey groupKey, HashSet<PathKey> instancePaths);
  void MarkConverted(PathKey instancePath);

  IEnumerable<PathKey> GetConvertedPaths();
  Dictionary<PathKey, List<PathKey>> BuildGroupToConvertedPaths();

  bool TryGetDefinitionWorld(PathKey groupKey, out double[] definitionWorld);
  void EnsureDefinitionWorld(PathKey groupKey, double[] definitionWorld);

  bool TryGetInstanceWorld(PathKey instancePath, out double[] instanceWorld);
  void SetInstanceWorld(PathKey instancePath, double[] instanceWorld);

  void RegisterInstanceObservation(
    PathKey groupKey,
    PathKey instancePath,
    double[] instanceWorld,
    PrimitiveProcessor processor
  );
}

public sealed class InstanceFragmentRegistry : IInstanceFragmentRegistry
{
  private readonly Dictionary<PathKey, PathKey> _pathToGroup = new(PathKey.Comparer);
  private readonly HashSet<PathKey> _converted = new(PathKey.Comparer);

  private readonly Dictionary<PathKey, double[]> _groupToDefinitionWorld = new(PathKey.Comparer);
  private readonly Dictionary<PathKey, double[]> _pathToInstanceWorld = new(PathKey.Comparer);
  private readonly Dictionary<PathKey, Aabb> _groupSignature = new(PathKey.Comparer);

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

  public bool TryGetDefinitionWorld(PathKey groupKey, out double[] definitionWorld) =>
    _groupToDefinitionWorld.TryGetValue(groupKey, out definitionWorld);

  public void EnsureDefinitionWorld(PathKey groupKey, double[] definitionWorld)
  {
    if (!_groupToDefinitionWorld.ContainsKey(groupKey))
    {
      _groupToDefinitionWorld[groupKey] = definitionWorld;
    }
  }

  public bool TryGetInstanceWorld(PathKey instancePath, out double[] instanceWorld) =>
    _pathToInstanceWorld.TryGetValue(instancePath, out instanceWorld);

  public void SetInstanceWorld(PathKey instancePath, double[] instanceWorld) =>
    _pathToInstanceWorld[instancePath] = instanceWorld;

  public void RegisterInstanceObservation(
    PathKey groupKey,
    PathKey instancePath,
    double[] instanceWorld,
    PrimitiveProcessor processor
  )
  {
    if (instanceWorld == null)
    {
      throw new ArgumentNullException(nameof(instanceWorld));
    }

    if (instanceWorld.Length != 16)
    {
      throw new ArgumentException("Expected 16 doubles for a 4x4 matrix.", nameof(instanceWorld));
    }

    var inv = Speckle.Converter.Navisworks.Helpers.GeometryHelpers.InvertRigid(instanceWorld);
    if (inv == null)
    {
      throw new InvalidOperationException(
        "InvertRigid returned null. You are calling a different method than expected."
      );
    }

    var sig = GeometryHelpers.ComputeUnbakedAabb(processor, inv);

    if (!sig.IsValid)
    {
      return;
    }

    if (!_groupSignature.TryGetValue(groupKey, out var first))
    {
      _groupSignature[groupKey] = sig;
      _groupToDefinitionWorld[groupKey] = instanceWorld;
      return;
    }

#if DEBUG
    const double EPS = 1e-6; // tune, maybe relative later
    if (!GeometryHelpers.NearlyEqual(first, sig, EPS))
    {
      System.Diagnostics.Debug.Fail($"Group {groupKey} signature mismatch. First {first} vs current {sig}");
    }
#endif
  }
}
