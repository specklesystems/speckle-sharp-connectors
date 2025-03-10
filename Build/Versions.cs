using Semver;
using static SimpleExec.Command;

namespace Build;

public static class Versions
{
  private static string? s_currentTag;
  private static SemVersion? s_currentVersion;

  public static async Task<string> GetCurrentTag()
  {
    if (s_currentTag is not null)
    {
      return s_currentTag;
    }
    var (currentTag, _) = await ReadAsync("git", "describe --tags");
    currentTag = currentTag.Trim();
    s_currentTag = currentTag;
    return s_currentTag;
  }

  public static async Task<SemVersion> ComputeVersion()
  {
    if (s_currentVersion is not null)
    {
      return s_currentVersion;
    }
    var currentTag = await GetCurrentTag();

    if (!SemVersion.TryParse(currentTag, SemVersionStyles.AllowLowerV, out var currentVersion))
    {
      throw new InvalidOperationException($"Could not parse version: '{currentTag}'");
    }
    s_currentVersion = currentVersion;
    return s_currentVersion;
  }

  private static string? s_currentFileVersion;

  public static async Task<string> ComputeFileVersion()
  {
    if (s_currentFileVersion is not null)
    {
      return s_currentFileVersion;
    }
    var currentVersion = await ComputeVersion();
    s_currentFileVersion = currentVersion.WithoutPrereleaseOrMetadata() + ".0";
    return s_currentFileVersion;
  }

  public static async Task<string> GetPreviousTag(string currentTag)
  {
    var (lastTag, _) = await ReadAsync("git", $"describe --abbrev=0 --tags {currentTag}^");
    lastTag = lastTag.Trim();

    return lastTag;
  }

  private static SemVersion? s_previousVersion;

  public static async Task<SemVersion> ComputePreviousVersion(string currentTag)
  {
    if (s_previousVersion is not null)
    {
      return s_previousVersion;
    }
    var lastTag = await GetPreviousTag(currentTag);

    if (!SemVersion.TryParse(lastTag, SemVersionStyles.AllowLowerV, out var lastVersion))
    {
      throw new InvalidOperationException($"Could not parse version: '{lastTag}'");
    }
    s_previousVersion = lastVersion;
    return s_previousVersion;
  }
}
