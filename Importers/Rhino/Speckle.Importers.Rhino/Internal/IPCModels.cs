using Speckle.Sdk;
using Speckle.Sdk.Api.GraphQL.Models;
using Speckle.Sdk.Credentials;

namespace Speckle.Importers.Rhino.Internal;

//Needs to be aligned between JobProcess and Rhino Importer
internal readonly struct ImporterArgs
{
  public required string FilePath { get; init; }
  public required string JobId { get; init; }
  public required string BlobId { get; init; }
  public required int Attempt { get; init; }
  public required string ResultsPath { get; init; }
  public required Project Project { get; init; }
  public required ModelIngestion Ingestion { get; init; }
  public required Account Account { get; init; }
  public required Application HostApplication { get; init; }
}

public readonly struct ImporterResponse
{
  public string? RootObjectId { get; init; }
  public string? ErrorMessage { get; init; }
}
