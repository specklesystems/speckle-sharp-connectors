using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Build;

public static class Solutions
{
#pragma warning disable CA1802
#pragma warning disable IDE1006
  private static readonly string DIRECTORY = Environment.CurrentDirectory;
#pragma warning restore IDE1006
#pragma warning restore CA1802
  public static async Task CompareConnectorsToLocal()
  {
    var localSln = await GetSolution("Local.sln");
    var connectorsSln = await GetSolution("Speckle.Connectors.sln");
    var localProjects = localSln.SolutionProjects.ToList();

    foreach (var value in connectorsSln.SolutionProjects)
    {
      var localProject = localProjects.FirstOrDefault(x => x.ActualDisplayName == value.ActualDisplayName);
      if (localProject is null)
      {
        throw new InvalidOperationException($"Could not find in LOCAL solution: {value.ActualDisplayName}");
      }

      if (value.ActualDisplayName != localProject.ActualDisplayName)
      {
        throw new InvalidOperationException(
          "Projects with different names have same Guid in solution: "
            + value.ActualDisplayName
            + " and "
            + localProject.ActualDisplayName
        );
      }
      localProjects.Remove(localProjects.Single(x => x.ActualDisplayName == value.ActualDisplayName));
    }

    void CheckAndRemoveKnown(string projectName)
    {
      var localProject = localProjects.FirstOrDefault(x => x.ActualDisplayName == projectName);
      if (localProject is null)
      {
        throw new InvalidOperationException($"Could not find in LOCAL solution: {projectName}");
      }
      localProjects.Remove(localProjects.Single(x => x.ActualDisplayName == projectName));
    }

    CheckAndRemoveKnown("Speckle.Objects");
    CheckAndRemoveKnown("Speckle.Sdk");
    CheckAndRemoveKnown("Speckle.Sdk.Dependencies");
    if (localProjects.Count != 0)
    {
      throw new InvalidOperationException(
        "Could not find in CONNECTOR solution: " + localProjects.First().ActualDisplayName
      );
    }
  }

  public static async Task GenerateSolutions()
  {
    await GenerateLocalSlnx();
    foreach (var group in Consts.ProjectGroups)
    {
      var connectors = await GetFullSlnx();
      var slug = group.HostAppSlug;
      await GenerateConnector(connectors, group, string.Concat(slug[0].ToString().ToUpper(), slug.AsSpan(1)));
    }
  }

  public static async Task GenerateLocalSlnx()
  {
    var connectors = await GetFullSlnx();
    connectors.AddProject("..\\speckle-sharp-sdk\\src\\Speckle.Objects\\Speckle.Objects.csproj");
    connectors.AddProject("..\\speckle-sharp-sdk\\src\\Speckle.Sdk\\Speckle.Sdk.csproj");
    connectors.AddProject("..\\speckle-sharp-sdk\\src\\Speckle.Sdk.Dependencies\\Speckle.Sdk.Dependencies.csproj");
    var sln = Path.Combine(DIRECTORY, "Local.slnx");
    await SolutionSerializers.SlnXml.SaveAsync(sln, connectors, default);
    sln = Path.Combine(DIRECTORY, "Local.sln");
    await SolutionSerializers.SlnFileV12.SaveAsync(sln, connectors, default);

    var revit = Consts.ProjectGroups.Single(x => x.HostAppSlug.Equals("revit"));
    await GenerateConnector(connectors, revit, "Revit.Local");
  }

  public static async Task GenerateConnector(SolutionModel connectors, ProjectGroup group, string? name)
  {
    var path = group.Projects[0].ProjectPath.Split('/');
    var folder = $"/{path[0]}/{path[1]}/";
    var foldersToRemove = connectors
      .SolutionFolders.Where(x =>
        //need base folder
        !x.Path.Equals("/Connectors/")
        //don't grab all
        && (x.Path.StartsWith("/Connectors/") && !x.Path.StartsWith(folder))
      )
      .ToList();
    foreach (var folderToRemove in foldersToRemove)
    {
      connectors.RemoveFolder(folderToRemove);
    }
    var sln = Path.Combine(DIRECTORY, $"Speckle.{name}.slnx");
    await SolutionSerializers.SlnXml.SaveAsync(sln, connectors, default);
  }

  public static async Task<SolutionModel> GetFullSlnx()
  {
    var connectorsSln = Path.Combine(DIRECTORY, "Speckle.Connectors.slnx");
    return await SolutionSerializers.SlnXml.OpenAsync(connectorsSln, default);
  }

  public static async Task<SolutionModel> GetSolution(string solutionName)
  {
    var connectorsSln = Path.Combine(DIRECTORY, solutionName);
    return await SolutionSerializers.SlnFileV12.OpenAsync(connectorsSln, default);
  }
}
