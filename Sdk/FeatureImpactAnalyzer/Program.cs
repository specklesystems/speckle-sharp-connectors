using System.CommandLine;
using Speckle.Sdk;

namespace FeatureImpactAnalyzer;

public static class Program
{
  public static async Task Main(string[] args)
  {
    var githubTokenOption = new Option<string>("--github-token", "GitHub Personal Access Token");
    var repositoryOption = new Option<string>("--repository", "Repository in owner/name format");
    var pullRequestOption = new Option<int>("--pull-request", "Pull request number");

    var rootCommand = new RootCommand("Side Effects PR Analyzer");
    rootCommand.AddOption(githubTokenOption);
    rootCommand.AddOption(repositoryOption);
    rootCommand.AddOption(pullRequestOption);

    rootCommand.SetHandler(
      async (token, repo, prNumber) =>
      {
        try
        {
          var (owner, name) = ParseRepository(repo);
          var analyzer = new GitHubPullRequestAnalyzer(token);
          Console.WriteLine($"Name: {name}");
          Console.WriteLine($"Owner: {owner}");
          Console.WriteLine($"Analyzing PR {prNumber}");

          var sideEffects = await analyzer.AnalyzePullRequestChangesAsync(owner, name, prNumber).ConfigureAwait(true);

          await analyzer
            .CommentPullRequestWithSideEffectsAsync(owner, name, prNumber, sideEffects)
            .ConfigureAwait(true);

          Console.WriteLine("Side effects analysis completed successfully.");
        }
        catch (Exception ex) when (!ex.IsFatal())
        {
          Console.Error.WriteLine($"Analysis failed: {ex.Message}");
          Environment.Exit(1);
        }
      },
      githubTokenOption,
      repositoryOption,
      pullRequestOption
    );

    await rootCommand.InvokeAsync(args).ConfigureAwait(true);
  }

  private static (string owner, string name) ParseRepository(string repository)
  {
    var parts = repository.Split('/');
    if (parts.Length != 2)
    {
      throw new ArgumentException("Repository must be in 'owner/name' format");
    }

    return (parts[0], parts[1]);
  }
}
