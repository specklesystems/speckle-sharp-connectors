namespace Speckle.Importers.JobProcessor.Domain;

/// <summary>
///DB model for the fileimport job.
/// </summary>
internal sealed class FileimportJob
{
  public required string Id { get; init; }
  public required string JobType { get; init; }
  public required FileimportPayload Payload { get; init; }
  public required JobStatus Status { get; init; }
  public required int Attempt { get; init; }
  public required int MaxAttempt { get; init; }
  public required DateTime CreatedAt { get; init; }
  public required DateTime UpdatedAt { get; init; }
}
