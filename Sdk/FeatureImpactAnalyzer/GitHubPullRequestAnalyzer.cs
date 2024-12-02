using System.Reflection;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Octokit;
using Speckle.Connectors.Common;
using Speckle.Sdk;

namespace FeatureImpactAnalyzer;

public record FeatureImpactReport(string MethodName, List<string> PotentialSideEffects);

public class GitHubPullRequestAnalyzer
{
  private readonly GitHubClient _client;

  public GitHubPullRequestAnalyzer(string githubToken)
  {
    _client = new GitHubClient(new ProductHeaderValue("FeatureImpactAnalyzer"))
    {
      Credentials = new Credentials(githubToken)
    };
  }

  public async Task<List<FeatureImpactReport>> AnalyzePullRequestChangesAsync(
    string repositoryOwner,
    string repositoryName,
    int pullRequestNumber
  )
  {
    GetAssemblies();
    GetMethods();

    var changedFiles = await _client
      .PullRequest.Files(repositoryOwner, repositoryName, pullRequestNumber)
      .ConfigureAwait(true);
    Console.WriteLine($"Changed Files Count:\n {changedFiles.Count}");

    var sideEffectReports = new List<FeatureImpactReport>();

    foreach (var file in changedFiles.Where(f => f.FileName.EndsWith(".cs")))
    {
      var methodSideEffects = AnalyzeMethodSideEffects(file.Patch);
      sideEffectReports.AddRange(methodSideEffects);
    }

    return sideEffectReports;
  }

  private List<FeatureImpactReport> AnalyzeMethodSideEffects(string filePatch)
  {
    var reports = new List<FeatureImpactReport>();
    var modifiedLines = ExtractModifiedLines(filePatch);
    var syntaxTree = CSharpSyntaxTree.ParseText(filePatch);
    var root = syntaxTree.GetRoot();
    var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToList();
    Console.WriteLine($"Found {methods.Count} methods");

    foreach (var method in methods)
    {
      // Get the line span of the method to compare
      var lineSpan = method.GetLocation().GetLineSpan();
      var startLine = lineSpan.StartLinePosition.Line;
      var endLine = lineSpan.EndLinePosition.Line;

      // Check if the method overlaps with modified lines
      if (modifiedLines.Any(line => line >= startLine && line <= endLine))
      {
        // Use reflection to check if the method has FeatureImpact attributes
        var methodName = method.Identifier.Text;
        var attributes = GetFeatureImpactAttributes(methodName);

        if (attributes.Count != 0)
        {
          reports.Add(new FeatureImpactReport(methodName, attributes));
        }
      }
    }

    return reports;
  }

  private List<int> ExtractModifiedLines(string filePatch)
  {
    var modifiedLines = new List<int>();
    var lines = filePatch.Split('\n');
    int currentLine = 0;

    foreach (var line in lines)
    {
#pragma warning disable CA1866
      if (line.StartsWith("+") && !line.StartsWith("+++"))
#pragma warning restore CA1866
      {
        // It's a modified line, add it to the list
        modifiedLines.Add(currentLine);
      }
      currentLine++;
    }

    return modifiedLines;
  }

  private List<MethodInfo> MethodsToCheck { get; } = new();
  private List<Assembly> Assemblies { get; } = new();

  private void GetAssemblies()
  {
    // Load all assemblies in the current AppDomain
    string? solutionDirectory = Path.GetFullPath(
      Path.Combine(Assembly.GetExecutingAssembly().Location, "..", "..", "..", "..", "..", "..")
    );

    if (!string.IsNullOrEmpty(solutionDirectory))
    {
      // Search for all DLLs in the parent directory and subdirectories
      var dllFiles = Directory
        .GetFiles(solutionDirectory, "*.dll", SearchOption.AllDirectories)
        .Where(dll =>
          (
            Path.GetFileName(dll).StartsWith("Speckle.Connectors.", StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(dll).StartsWith("Speckle.Converters.", StringComparison.OrdinalIgnoreCase)
          ) && !Path.GetFileName(dll).Contains("Test", StringComparison.OrdinalIgnoreCase) // Exclude test assemblies
        );

      foreach (var dll in dllFiles)
      {
        try
        {
          var assemblyName = AssemblyName.GetAssemblyName(dll);
          if (!Assemblies.Any(a => a.FullName == assemblyName.FullName))
          {
            var loadedAssembly = Assembly.LoadFrom(dll);
            Assemblies.Add(loadedAssembly);
            Console.WriteLine($"Loaded assembly: {loadedAssembly.FullName}");
          }
        }
        catch (BadImageFormatException)
        {
          Console.WriteLine($"Skipping invalid assembly: {dll}");
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          Console.WriteLine($"Failed to load assembly: {dll}. Error: {ex.Message}");
        }
      }
    }
  }

  private void GetMethods()
  {
    foreach (var assembly in Assemblies)
    {
      try
      {
        foreach (var type in assembly.GetTypes())
        {
          Console.WriteLine($"Checking type: {type.FullName}");
          foreach (
            var method in type.GetMethods(
              BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
            )
          )
          {
            var attributes = method.GetCustomAttributes(typeof(FeatureImpactAttribute), true);
            if (attributes.Length != 0)
            {
              MethodsToCheck.Add(method);
            }
          }
        }
      }
      catch (ReflectionTypeLoadException ex) // .net standard problem?
      {
        Console.WriteLine($"Skipping problematic assembly: {assembly.FullName}");
        foreach (var loaderException in ex.LoaderExceptions)
        {
          Console.WriteLine($"Loader Exception: {loaderException?.Message}");
        }
      }
    }
  }

  private List<string> GetFeatureImpactAttributes(string methodName)
  {
    var impactedFeatures = new List<string>();

    if (MethodsToCheck.Any(m => m.Name == methodName))
    {
      var method = MethodsToCheck.First(m => m.Name == methodName);
      var attributes = method.GetCustomAttributes(typeof(FeatureImpactAttribute), true);
      foreach (FeatureImpactAttribute attribute in attributes.Cast<FeatureImpactAttribute>())
      {
        impactedFeatures.AddRange(attribute.Features);
      }
    }

    return impactedFeatures;
  }

  public async Task CommentPullRequestWithSideEffectsAsync(
    string repositoryOwner,
    string repositoryName,
    int pullRequestNumber,
    List<FeatureImpactReport> sideEffects
  )
  {
    Console.WriteLine($"Side effect count: {sideEffects.Count}");
    if (sideEffects.Count == 0)
    {
      var commentAllGoodBody = "✅ All good!\n\n";
      await _client
        .Issue.Comment.Create(repositoryOwner, repositoryName, pullRequestNumber, commentAllGoodBody)
        .ConfigureAwait(true);
      return;
    }

    var commentBody = "⚠️ Potential Side Effects Detected:\n\n";
    foreach (var report in sideEffects)
    {
      commentBody += $"- Method `{report.MethodName}` might affect: {string.Join(", ", report.PotentialSideEffects)}\n";
    }

    await _client
      .Issue.Comment.Create(repositoryOwner, repositoryName, pullRequestNumber, commentBody)
      .ConfigureAwait(true);
  }
}
