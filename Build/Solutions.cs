using Microsoft.Build.Construction;
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

  public static async Task GenerateLocalSlnx()
  {
    var connectorsSln = Path.Combine(Environment.CurrentDirectory, "Speckle.Connectors.sln");
    var connectors = await SolutionSerializers.SlnXml.OpenAsync(connectorsSln, default);
    connectors.AddProject("..\\speckle-sharp-sdk\\src\\Speckle.Objects\\Speckle.Objects.csproj");
    connectors.AddProject("..\\speckle-sharp-sdk\\src\\Speckle.Sdk\\Speckle.Sdk.csproj");
    connectors.AddProject("..\\speckle-sharp-sdk\\src\\Speckle.Sdk.Dependencies\\Speckle.Sdk.Dependencies.csproj");
    var localSln = Path.Combine(Environment.CurrentDirectory, "Local.slnx");
    using var x = File.OpenWrite(localSln);
    await SolutionSerializers.SlnXml.SaveAsync(x, connectors, default);
  }
}
