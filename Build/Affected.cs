using GlobExpressions;
using Microsoft.Build.Construction;
using Semver;
using static SimpleExec.Command;

namespace Build;

public static class Affected
{
  public static readonly string Root = Environment.CurrentDirectory;
  public const string AFFECTED_PROJECT = "affected.proj";

  private static IEnumerable<string> GetAffectedProjects()
  {
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    Console.WriteLine("Affected project file: " + projFile);
    var project = ProjectRootElement.Open(projFile) ?? throw new InvalidOperationException();
    var references = project.ItemGroups.SelectMany(x => x.Items).Where(x => x.ItemType == "ProjectReference");

    foreach (var refe in references)
    {
      var referencePath = refe.Include[(Root.Length + 1)..];
      referencePath = Path.GetDirectoryName(referencePath) ?? throw new InvalidOperationException();
      if (Path.DirectorySeparatorChar != '/')
      {
        referencePath = referencePath.Replace(Path.DirectorySeparatorChar, '/');
      }

      yield return referencePath;
    }
  }
  
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

  public static async Task<IEnumerable<string>> GetProjects()
  {
    await ComputeAffected();
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      var references = GetAffectedProjects();
      return references.Where(x => x.EndsWith(".Tests.csproj")); 
    }
    return Glob.Files(Root, "**/*.Tests.csproj");
  }

  public static async Task<InstallerProject[]> GetInstallerProjects()
  {
    await ComputeAffected();
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      var references = GetAffectedProjects().ToList();
      var projs = new List<InstallerProject>();

      foreach (var referencePath in references)
      {
        Console.WriteLine($"Candidate project: {referencePath}");
      }

      foreach (var manifest in Consts.InstallerManifests)
      {
        var assets = new List<InstallerAsset>();
        foreach (var referencePath in references)
        {
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

  private static bool s_affectedComputed;

  public static async Task ComputeAffected()
  {
    if (s_affectedComputed)
    {
      return;
    }

    var (currentTag, _) = await ReadAsync("git", "describe --tags");
    currentTag = currentTag.Trim();
    var version = await Versions.ComputeVersion();
    var currentVersion = SemVersion.Parse(version).WithoutPrereleaseOrMetadata();
    var (lastTag, _) = await ReadAsync("git", $"describe --abbrev=0 --tags {currentTag}^");
    lastTag = lastTag.Trim();

    if (!SemVersion.TryParse(lastTag, SemVersionStyles.AllowLowerV, out var lastVersion))
    {
      Console.WriteLine($"Could not parse version: '{lastTag}'");
      s_affectedComputed = true;
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
      s_affectedComputed = true;
      return;
    }

    if (sort != 1)
    {
      Console.WriteLine($"Current version {currentVersion} is not greater than: {lastVersion}");
      s_affectedComputed = true;
      return;
    }

    var majorEquals = currentVersion.Major == lastVersion.Major;
    var minorEquals = currentVersion.Minor == lastVersion.Minor;
    if (!majorEquals)
    {
      Console.WriteLine($"Current version {currentVersion} is not matching major version: {lastVersion}");
      s_affectedComputed = true;
      return;
    }

    if (minorEquals)
    {
      var (currentCommit, _) = await ReadAsync("git", $"rev-list -n 1 {currentTag.Trim()}");
      var (lastCommit, _) = await ReadAsync("git", $"rev-list -n 1 {lastTag.Trim()}");
      await RunAsync("dotnet", $"affected --from {currentCommit.Trim()} --to {lastCommit.Trim()}", Root);
    }

    s_affectedComputed = true;
  }
}
