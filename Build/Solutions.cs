using Microsoft.Build.Construction;

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
    if (localProjects.Count != 0)
    {
      throw new InvalidOperationException(
        "Could not find in CONNECTOR solution: " + localProjects.First().Value.ProjectName
      );
    }
  }
}
