using Microsoft.Build.Construction;

namespace Build;

public static class Affected
{
  public static readonly string Root = Environment.CurrentDirectory;
  public const string AFFECTED_PROJECT = "affected.proj";

  public static string[] GetSolutions()
  {
    var projFile = Path.Combine(Root, AFFECTED_PROJECT);
    if (File.Exists(projFile))
    {
      Console.WriteLine("Affected project file: " + projFile);
      return [projFile];
    }

    Console.WriteLine("Using solutions: " + string.Join(',', Consts.Solutions));
    return Consts.Solutions;
  }

  public static InstallerProject[] GetInstallerProjects()
  {
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
}
