namespace Speckle.Importers.JobProcessor.Domain;

/// <summary>
/// Payload for the fileimport job
/// </summary>
public sealed class FileimportPayload
{
  public required string JobId { get; init; }
  public required string Token { get; init; }
  public required string BlobId { get; init; }
  public required string JobType { get; init; }
  public required string ModelId { get; init; }
  public required string FileName { get; init; }
  public required string FileType { get; init; }
  public required string ProjectId { get; init; }
  public required Uri ServerUrl { get; init; }
  public required int PayloadVersion { get; init; }
  public required int TimeOutSeconds { get; init; }
}
