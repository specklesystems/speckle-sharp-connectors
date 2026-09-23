namespace Speckle.Converters.TSDShared;

/// <summary>
/// Per-send tally of elements that kept their wireframe display value although volumetric geometry was requested,
/// keyed by "<c>elementType/reason</c>" so one aggregated warning per send can say which shapes are missing (ENG-9048).
/// </summary>
public sealed class TsdExtrusionFallbackTracker
{
  private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

  public int Total { get; private set; }

  public IReadOnlyDictionary<string, int> Counts => _counts;

  public void Record(string elementType, string reason)
  {
    string key = $"{elementType}/{reason}";
    _counts[key] = _counts.TryGetValue(key, out int count) ? count + 1 : 1;
    Total++;
  }
}
