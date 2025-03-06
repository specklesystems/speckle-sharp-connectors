using Microsoft.Build.Construction;
using Semver;
using static SimpleExec.Command;

namespace Build;

public static class Affected
{
  public static readonly string Root = Environment.CurrentDirectory;
  public const string AFFECTED_PROJECT = "affected.proj";

  public static async Task<string[]> GetSolutions()
  {
    await ComputeAffected();
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      Console.WriteLine("Affected project file: " + projFile);
      return [projFile];
    }

    Console.WriteLine("Using solutions: " + string.Join(',', Consts.Solutions));
    return Consts.Solutions;
  }

  public static async Task<InstallerProject[]> GetInstallerProjects()
  {
    await ComputeAffected();
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      Console.WriteLine("Affected project file: " + projFile);
      var project = ProjectRootElement.Open(projFile) ?? throw new InvalidOperationException();
      var references = project
        .ItemGroups.SelectMany(x => x.Items)
        .Where(x => x.ItemType == "ProjectReference")
        .ToList();
      var projs = new List<InstallerProject>();

      foreach (var refe in references)
      {
        Console.WriteLine($"Candidate project: {refe.Include}");
      }

      foreach (var manifest in Consts.InstallerManifests)
      {
        var assets = new List<InstallerAsset>();
        foreach (var refe in references)
        {
          var referencePath = refe.Include[(Root.Length + 1)..];
          referencePath = Path.GetDirectoryName(referencePath) ?? throw new InvalidOperationException();
          if (Path.DirectorySeparatorChar != '/')
          {
            referencePath = referencePath.Replace(Path.DirectorySeparatorChar, '/');
          }

          foreach (var proj in manifest.Projects)
          {
            if (proj.ProjectPath.Contains(referencePath))
            {
              assets.Add(proj);
            }
          }
        }

        if (assets.Count > 0)
        {
          projs.Add(manifest with { Projects = assets });
        }
      }

      foreach (var proj in projs.SelectMany(x => x.Projects))
      {
        Console.WriteLine("Affected project being built: " + proj);
      }

      if (projs.Count > 0)
      {
        return projs.ToArray();
      }
    }

    Console.WriteLine("Using all installer manifests: " + string.Join(',', Consts.InstallerManifests));
    return Consts.InstallerManifests;
  }

  public static async Task ComputeAffected()
  {
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      return;
    }
    var (currentTag, _) = await ReadAsync("git", "describe --tags");
    currentTag = currentTag.Trim();
    var version = await Affected.ComputeVersion();
    var currentVersion = SemVersion.Parse(version).WithoutPrereleaseOrMetadata();
    var (lastTag, _) = await ReadAsync("git", $"describe --abbrev=0 --tags {currentTag}^");
    lastTag = lastTag.Trim();

    if (!SemVersion.TryParse(lastTag, SemVersionStyles.AllowLowerV, out var lastVersion))
    {
      Console.WriteLine($"Could not parse version: '{lastTag}'");
      return;
    }
    Console.WriteLine($"Last tag: {lastTag}, Current tag: {currentTag}");

    lastVersion = lastVersion.WithoutPrereleaseOrMetadata();
    currentVersion = currentVersion.WithoutPrereleaseOrMetadata();
    Console.WriteLine($"Last parsed version: {lastVersion}, Current parsed version: {currentVersion}");
    var sort = currentVersion.CompareSortOrderTo(lastVersion);
    Console.WriteLine($"Sort: {sort}");
    if (sort == 0)
    {
      Console.WriteLine($"Current version {currentVersion} is equal to: {lastVersion}");
      return;
    }
    if (sort != 1)
    {
      Console.WriteLine($"Current version {currentVersion} is not greater than: {lastVersion}");
      return;
    }

    var majorEquals = currentVersion.Major == lastVersion.Major;
    var minorEquals = currentVersion.Minor == lastVersion.Minor;
    if (!majorEquals)
    {
      Console.WriteLine($"Current version {currentVersion} is not matching major version: {lastVersion}");
      return;
    }

    if (minorEquals)
    {
      var (currentCommit, _) = await ReadAsync("git", $"rev-list -n 1 {currentTag.Trim()}");
      var (lastCommit, _) = await ReadAsync("git", $"rev-list -n 1 {lastTag.Trim()}");
      await RunAsync("dotnet", $"affected --from {currentCommit.Trim()} --to {lastCommit.Trim()}", Root);
    }
  }

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
    var version = await Affected.ComputeVersion();
    var currentVersion = SemVersion.Parse(version).WithoutPrereleaseOrMetadata();
    s_currentFileVersion = currentVersion.ToString() + ".0";
    return s_currentFileVersion;
  }
}
