using GlobExpressions;
using Microsoft.Build.Construction;
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

  public static async Task<IEnumerable<string>> GetTestProjects()
  {
    await ComputeAffected();
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      var references = GetAffectedProjects();
      return references.Where(x => x.Contains("Tests"));
    }
    return Glob.Files(Root, "**/*.Tests.csproj");
  }

  public static async Task<ProjectGroup[]> GetAffectedProjectGroups()
  {
    await ComputeAffected();
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      var references = GetAffectedProjects().ToList();
      var groups = new List<ProjectGroup>();
      foreach (var projectGroup in Consts.ProjectGroups)
      {
        foreach (var referencePath in references)
        {
          if (projectGroup.Projects.Any(x => x.ProjectPath.Contains(referencePath)))
          {
            groups.Add(projectGroup);
            break;
          }
        }
      }

      foreach (var group in groups)
      {
        Console.WriteLine("Affected project group being built: " + group.HostAppSlug);
      }

      if (groups.Count > 0)
      {
        return groups.ToArray();
      }
    }

    Console.WriteLine("Using all project groups: " + string.Join(',', Consts.ProjectGroups));
    return Consts.ProjectGroups;
  }

  private static bool s_affectedComputed;

  public static async Task ComputeAffected()
  {
    if (s_affectedComputed)
    {
      return;
    }

    var currentTag = await Versions.GetCurrentTag();
    var currentVersion = await Versions.ComputeVersion();
    var lastTag = await Versions.GetPreviousTag(currentTag);
    var lastVersion = await Versions.ComputePreviousVersion(currentTag);

    Console.WriteLine($"Last tag: {lastTag}, Current tag: {currentTag}");
    Console.WriteLine($"Last parsed version: {lastVersion}, Current parsed version: {currentVersion}");

    var sort = currentVersion.CompareSortOrderTo(lastVersion);
    if (sort == -1)
    {
      Console.WriteLine($"Current version {currentVersion} is less than: {lastVersion}");
      s_affectedComputed = true;
      return;
    }

    var majorEquals = currentVersion.Major == lastVersion.Major;
    if (!majorEquals)
    {
      Console.WriteLine($"Current version {currentVersion} is not matching major version: {lastVersion}");
      s_affectedComputed = true;
      return;
    }

    //use tags no matter the version if major versions match
    var (currentCommit, _) = await ReadAsync("git", $"rev-list -n 1 {currentTag}");
    var (lastCommit, _) = await ReadAsync("git", $"rev-list -n 1 {lastTag}");
    await RunAsync("dotnet", $"affected -v --from {currentCommit.Trim()} --to {lastCommit.Trim()}", Root);

    s_affectedComputed = true;
  }
}
