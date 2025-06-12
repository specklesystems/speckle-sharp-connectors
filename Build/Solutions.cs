﻿using Microsoft.Build.Construction;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Build;

public static class Solutions
{
  private static bool ValidProjects(KeyValuePair<string, ProjectInSolution> projectInSolution) =>
    projectInSolution.Value.ProjectType == SolutionProjectType.KnownToBeMSBuildFormat;

  public static void CompareConnectorsToLocal()
  {
    var localSln = SolutionFile.Parse(Path.Combine(Environment.CurrentDirectory, "Local.sln"));
    var connectorsSln = SolutionFile.Parse(Path.Combine(Environment.CurrentDirectory, "Speckle.Connectors.sln"));
    var localProjects = localSln.ProjectsByGuid.Where(ValidProjects).ToDictionary();

    foreach ((string? _, ProjectInSolution? value) in connectorsSln.ProjectsByGuid.Where(ValidProjects))
    {
      var localProject = localProjects.Values.FirstOrDefault(x => x.ProjectName == value.ProjectName);
      if (localProject is null)
      {
        throw new InvalidOperationException($"Could not find in LOCAL solution: {value.ProjectName}");
      }

      if (value.ProjectName != localProject.ProjectName)
      {
        throw new InvalidOperationException(
          "Projects with different names have same Guid in solution: "
            + value.ProjectName
            + " and "
            + localProject.ProjectName
        );
      }
      localProjects.Remove(localProjects.Single(x => x.Value.ProjectName == value.ProjectName).Key);
    }

    void CheckAndRemoveKnown(string projectName)
    {
      var localProject = localProjects.Values.FirstOrDefault(x => x.ProjectName == projectName);
      if (localProject is null)
      {
        throw new InvalidOperationException($"Could not find in LOCAL solution: {projectName}");
      }
      localProjects.Remove(localProjects.Single(x => x.Value.ProjectName == projectName).Key);
    }

    CheckAndRemoveKnown("Speckle.Objects");
    CheckAndRemoveKnown("Speckle.Sdk");
    CheckAndRemoveKnown("Speckle.Sdk.Dependencies");
    if (localProjects.Count != 0)
    {
      throw new InvalidOperationException(
        "Could not find in CONNECTOR solution: " + localProjects.First().Value.ProjectName
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
    var sln = Path.Combine(Environment.CurrentDirectory, "Local.slnx");
    await SolutionSerializers.SlnXml.SaveAsync(sln, connectors, default);
    sln = Path.Combine(Environment.CurrentDirectory, "Local.sln");
    await SolutionSerializers.SlnFileV12.SaveAsync(sln, connectors, default);

    var revit = Consts.ProjectGroups.Single(x => x.HostAppSlug.Equals("revit"));
    await GenerateConnector(connectors, revit, "Revit.Local");
    await GenerateMacSolutions();
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
    var sln = Path.Combine(Environment.CurrentDirectory, $"Speckle.{name}.slnx");
    await SolutionSerializers.SlnXml.SaveAsync(sln, connectors, default);
  }

  public static async Task<SolutionModel> GetFullSlnx()
  {
    var connectorsSln = Path.Combine(Environment.CurrentDirectory, "Speckle.Connectors.slnx");
    return await SolutionSerializers.SlnXml.OpenAsync(connectorsSln, default);
  }

  public static async Task GenerateMacSolutions()
  {
    var connectors = await GetFullSlnx();
    var foldersToRemove = connectors
      .SolutionFolders.Where(x =>
        //need base folder
        !x.Path.Equals("/Connectors/")
        //don't grab all
        && (x.Path.StartsWith("/Connectors/"))
      )
      .ToList();
    foreach (var folderToRemove in foldersToRemove)
    {
      connectors.RemoveFolder(folderToRemove);
    }
    var sln = Path.Combine(Environment.CurrentDirectory, $"Speckle.Connectors.Mac.slnx");
    await SolutionSerializers.SlnXml.SaveAsync(sln, connectors, default);
  }
}
