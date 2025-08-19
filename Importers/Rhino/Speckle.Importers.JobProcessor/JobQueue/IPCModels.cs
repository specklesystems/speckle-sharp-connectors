using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.JobProcessor.JobQueue;

//Needs to be aligned between JobProcess and Rhino Importer
internal readonly struct ImporterArgs
{
  public string FilePath { get; init; }
  public string ResultsPath { get; init; }
  public string ProjectId { get; init; }
  public string ModelId { get; init; }
  public Account Account { get; init; }
}

public readonly struct ImporterResponse
{
  public Version? Version { get; init; }
  public Exception? Exception { get; init; }
}
