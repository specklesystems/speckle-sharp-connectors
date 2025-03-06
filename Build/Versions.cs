using Semver;
using static SimpleExec.Command;

namespace Build;

public static class Versions
{
  private static string? s_currentVersion;

  public static async Task<string> ComputeVersion()
  {
    if (s_currentVersion is not null)
    {
      return s_currentVersion;
    }
    var (currentTag, _) = await ReadAsync("git", "describe --tags");
    currentTag = currentTag.Trim();

    if (!SemVersion.TryParse(currentTag, SemVersionStyles.AllowLowerV, out var currentVersion))
    {
      throw new InvalidOperationException($"Could not parse version: '{currentTag}'");
    }
    s_currentVersion = currentVersion.ToString();
    return s_currentVersion;
  }

  private static string? s_currentFileVersion;

  public static async Task<string> ComputeFileVersion()
  {
    if (s_currentFileVersion is not null)
    {
      return s_currentFileVersion;
    }
    var version = await ComputeVersion();
    var currentVersion = SemVersion.Parse(version).WithoutPrereleaseOrMetadata();
    s_currentFileVersion = currentVersion.ToString() + ".0";
    return s_currentFileVersion;
  }
}
