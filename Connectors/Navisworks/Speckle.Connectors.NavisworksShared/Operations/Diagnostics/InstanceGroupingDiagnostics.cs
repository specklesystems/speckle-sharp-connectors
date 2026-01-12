using System.Text;
using Speckle.Converter.Navisworks.Paths;

namespace Speckle.Connector.Navisworks.Operations.Diagnostics;

/// <summary>
/// Diagnostic information about instance grouping behavior.
/// Used to understand and debug why grouping may fail with large selections.
/// </summary>
public class InstanceGroupingDiagnostics
{
  public int TotalGroupsCreated { get; init; }
  public int SingleMemberGroups { get; init; }
  public int MultiMemberGroups { get; init; }
  public int TotalPathsProcessed { get; init; }
  public Dictionary<PathKey, int> GroupMemberCounts { get; init; } = new(PathKey.Comparer);
  public int LargestGroupSize { get; init; }
  public int TotalInstancesExpected { get; init; }
  public double GroupingEfficiency { get; init; }

  /// <summary>
  /// Generates a detailed diagnostic report of grouping behavior.
  /// </summary>
  public string GenerateReport()
  {
    var sb = new StringBuilder();
    sb.AppendLine("╔════════════════════════════════════════════════════════════════╗");
    sb.AppendLine("║         INSTANCE GROUPING DIAGNOSTICS REPORT                  ║");
    sb.AppendLine("╚════════════════════════════════════════════════════════════════╝");
    sb.AppendLine();

    // Summary Statistics
    sb.AppendLine("SUMMARY:");
    sb.AppendLine($"  Total Groups Created:      {TotalGroupsCreated, 6}");
    sb.AppendLine(
      $"  Single-Member Groups:      {SingleMemberGroups, 6} ({GetPercentage(SingleMemberGroups, TotalGroupsCreated), 5:F1}%)"
    );
    sb.AppendLine(
      $"  Multi-Member Groups:       {MultiMemberGroups, 6} ({GetPercentage(MultiMemberGroups, TotalGroupsCreated), 5:F1}%)"
    );
    sb.AppendLine($"  Total Paths Processed:     {TotalPathsProcessed, 6}");
    sb.AppendLine($"  Largest Group Size:        {LargestGroupSize, 6}");
    sb.AppendLine($"  Grouping Efficiency:       {GroupingEfficiency, 5:F1}%");
    sb.AppendLine();

    // Problem Detection
    sb.AppendLine("ANALYSIS:");
    if (SingleMemberGroups == TotalGroupsCreated && TotalGroupsCreated > 10)
    {
      sb.AppendLine("  ⚠️  CRITICAL: ALL groups are single-member!");
      sb.AppendLine("      This indicates grouping logic is NOT detecting shared geometry.");
      sb.AppendLine("      Expected behavior: multiple items should share group definitions.");
    }
    else if (SingleMemberGroups > TotalGroupsCreated * 0.9 && TotalGroupsCreated > 10)
    {
      sb.AppendLine("  ⚠️  WARNING: Over 90% of groups are single-member.");
      sb.AppendLine("      Grouping is mostly ineffective - very few instances detected.");
    }
    else if (MultiMemberGroups > 0)
    {
      sb.AppendLine("  ✓  OK: Instance grouping is working.");
      sb.AppendLine($"      {MultiMemberGroups} groups have multiple instances.");
    }
    sb.AppendLine();

    // Top Groups
    if (MultiMemberGroups > 0)
    {
      sb.AppendLine("TOP 10 GROUPS BY INSTANCE COUNT:");
      var top10 = GroupMemberCounts.Where(kvp => kvp.Value > 1).OrderByDescending(kvp => kvp.Value).Take(10);

      int rank = 1;
      foreach (var kvp in top10)
      {
        sb.AppendLine($"  {rank, 2}. Group {kvp.Key.ToHashString()}: {kvp.Value, 4} instances");
        rank++;
      }
      sb.AppendLine();
    }

    // Histogram
    sb.AppendLine("INSTANCE COUNT HISTOGRAM:");
    sb.AppendLine("  (Shows how many groups have N instances)");
    var histogram = GroupMemberCounts
      .Values.GroupBy(count => count)
      .OrderBy(g => g.Key)
      .Select(g => new { InstanceCount = g.Key, GroupCount = g.Count() })
      .ToList();

    foreach (var bucket in histogram.Take(20)) // Limit to first 20 buckets
    {
      string bar = new string('█', Math.Min(bucket.GroupCount / 10, 50)); // Scale for display
      sb.AppendLine($"  {bucket.InstanceCount, 4} instance(s): {bucket.GroupCount, 5} group(s) {bar}");
    }

    if (histogram.Count > 20)
    {
      sb.AppendLine($"  ... ({histogram.Count - 20} more buckets not shown)");
    }

    sb.AppendLine();
    sb.AppendLine("═══════════════════════════════════════════════════════════════");

    return sb.ToString();
  }

  /// <summary>
  /// Generates a compact one-line summary for logging.
  /// </summary>
  public string GenerateSummary() =>
    $"Groups: {TotalGroupsCreated} total ({MultiMemberGroups} multi-member, {SingleMemberGroups} single-member), "
    + $"Largest: {LargestGroupSize}, Efficiency: {GroupingEfficiency:F1}%";

  /// <summary>
  /// Checks if grouping is working as expected.
  /// </summary>
  public bool IsGroupingEffective()
  {
    // If we have more than 10 items and at least some multi-member groups, grouping is working
    if (TotalGroupsCreated > 10)
    {
      return MultiMemberGroups > 0 && GroupingEfficiency > 10.0;
    }

    // For small selections, it's hard to tell
    return true;
  }

  /// <summary>
  /// Gets recommendations for fixing grouping issues.
  /// </summary>
  public List<string> GetRecommendations()
  {
    var recommendations = new List<string>();

    if (SingleMemberGroups == TotalGroupsCreated && TotalGroupsCreated > 10)
    {
      recommendations.Add("CRITICAL: Fragment-based grouping is not working.");
      recommendations.Add("- Check if DiscoverInstancePathsFromFragments returns only 1 member per path");
      recommendations.Add("- Consider implementing AABB-based grouping as fallback");
      recommendations.Add("- Verify Navisworks model has actual instances (not unique geometry)");
    }
    else if (SingleMemberGroups > TotalGroupsCreated * 0.9)
    {
      recommendations.Add("WARNING: Low grouping efficiency detected.");
      recommendations.Add("- Some instance detection is working but not optimal");
      recommendations.Add("- Review fragment path comparison logic");
      recommendations.Add("- Check if selection includes mix of instanced/unique geometry");
    }

    if (LargestGroupSize > 100)
    {
      recommendations.Add($"INFO: Large instance group detected ({LargestGroupSize} instances)");
      recommendations.Add("- This is expected for repeated elements");
      recommendations.Add("- Ensure definition geometry is created only once");
    }

    return recommendations;
  }

  private static double GetPercentage(int part, int total) => total == 0 ? 0 : (double)part / total * 100.0;

  public override string ToString() => GenerateSummary();
}

/// <summary>
/// Builder for creating InstanceGroupingDiagnostics from raw data.
/// </summary>
public class InstanceGroupingDiagnosticsBuilder
{
  private readonly Dictionary<PathKey, int> _groupMemberCounts = new(PathKey.Comparer);
  private int _totalPathsProcessed;

  public void RecordGroup(PathKey groupKey, int memberCount)
  {
    _groupMemberCounts[groupKey] = memberCount;
    _totalPathsProcessed++;
  }

  public InstanceGroupingDiagnostics Build()
  {
    var singleMemberGroups = _groupMemberCounts.Count(kvp => kvp.Value == 1);
    var multiMemberGroups = _groupMemberCounts.Count(kvp => kvp.Value > 1);
    var largestGroupSize = _groupMemberCounts.Values.Count != 0 ? _groupMemberCounts.Values.Max() : 0;
    var totalInstancesExpected = _groupMemberCounts.Values.Sum();

    // Efficiency: how many definitions vs how many instances
    // Perfect efficiency (100%) = 1 definition per 100 instances
    // Poor efficiency (near 0%) = 1 definition per 1 instance
    double efficiency = 0;
    if (totalInstancesExpected > 0)
    {
      // Groups with > 1 member contribute to efficiency
      var instancedItems = _groupMemberCounts.Where(kvp => kvp.Value > 1).Sum(kvp => kvp.Value);
      efficiency = totalInstancesExpected > 0 ? ((double)instancedItems / totalInstancesExpected) * 100.0 : 0;
    }

    return new InstanceGroupingDiagnostics
    {
      TotalGroupsCreated = _groupMemberCounts.Count,
      SingleMemberGroups = singleMemberGroups,
      MultiMemberGroups = multiMemberGroups,
      TotalPathsProcessed = _totalPathsProcessed,
      GroupMemberCounts = new Dictionary<PathKey, int>(_groupMemberCounts, PathKey.Comparer),
      LargestGroupSize = largestGroupSize,
      TotalInstancesExpected = totalInstancesExpected,
      GroupingEfficiency = efficiency
    };
  }
}
