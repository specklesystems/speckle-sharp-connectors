using Speckle.Sdk.Api.GraphQL.Models;

namespace Speckle.Importers.JobProcessor.JobHandlers;

public sealed class MaxAttemptsExceededException : Exception
{
  public MaxAttemptsExceededException() { }

  public MaxAttemptsExceededException(string? message)
    : base(message) { }

  public MaxAttemptsExceededException(string? message, Exception? innerException)
    : base(message, innerException) { }
}

public sealed class JobTimeoutException : Exception
{
  public JobTimeoutException() { }

  public JobTimeoutException(string? message)
    : base(message) { }

  public JobTimeoutException(string? message, Exception? innerException)
    : base(message, innerException) { }
}

public class IngestionCancelledException : Exception
{
  public required ModelIngestion Ingestion { get; init; }

  public IngestionCancelledException() { }

  public IngestionCancelledException(string? message)
    : base(message) { }

  public IngestionCancelledException(string? message, Exception? innerException)
    : base(message, innerException) { }
}
