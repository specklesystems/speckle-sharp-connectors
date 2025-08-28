using Speckle.Sdk.Credentials;
using Version = Speckle.Sdk.Api.GraphQL.Models.Version;

namespace Speckle.Importers.Rhino.Internal;

//Needs to be aligned between JobProcess and Rhino Importer
internal readonly struct ImporterArgs
{
  public required string FilePath { get; init; }
  public required string JobId { get; init; }
  public required string BlobId { get; init; }
  public required int Attempt { get; init; }
  public required string ResultsPath { get; init; }
  public required string ProjectId { get; init; }
  public required string ModelId { get; init; }
  public required Account Account { get; init; }
}

public readonly struct ImporterResponse
{
  public Version? Version { get; init; }
  public string? ErrorMessage { get; init; }
}
