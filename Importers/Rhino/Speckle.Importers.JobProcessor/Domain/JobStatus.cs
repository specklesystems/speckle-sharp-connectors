namespace Speckle.Importers.JobProcessor.Domain;

/// <summary>
/// Status enumeration for the job.
/// </summary>
internal enum JobStatus
{
  QUEUED,
  PROCESSING,
  SUCCEEDED,
  FAILED,
}
