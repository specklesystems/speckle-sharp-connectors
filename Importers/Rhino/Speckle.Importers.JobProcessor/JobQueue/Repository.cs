using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Speckle.Importers.JobProcessor.Domain;

namespace Speckle.Importers.JobProcessor.JobQueue;

internal sealed class Repository(ILogger<Repository> logger)
{
  public async Task<NpgsqlConnection> SetupConnection(CancellationToken cancellationToken)
  {
    const string ENV_VAR = "FILEIMPORT_QUEUE_POSTGRES_URL";
    string fileImportQueuePostgresUrl =
      Environment.GetEnvironmentVariable(ENV_VAR)
      ?? throw new ArgumentException($"Expected {ENV_VAR} environment variable to be set");

    string connectionString = ParseConnectionString(new(fileImportQueuePostgresUrl));
    var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
    return connection;
  }

  private static string ParseConnectionString(Uri connectionUrl)
  {
    if (connectionUrl.Scheme is not "postgres" and not "postgresql")
    {
      throw new ArgumentException("Invalid URI scheme. Expected 'postgres' or 'postgresql'.", nameof(connectionUrl));
    }
    var userInfo = connectionUrl.UserInfo.Split(':');
    if (userInfo.Length != 2)
    {
      throw new ArgumentException("Invalid URI format: missing username or password.");
    }

    var builder = new NpgsqlConnectionStringBuilder
    {
      Host = connectionUrl.Host,
      Port = connectionUrl.Port > 0 ? connectionUrl.Port : 5432, // Default to 5432 if not specified
      Username = userInfo[0],
      Password = userInfo[1],
      Database = connectionUrl.AbsolutePath.TrimStart('/')
    };
    return builder.ConnectionString;
  }

  public async Task<FileimportJob?> GetNextJob(IDbConnection connection, CancellationToken cancellationToken)
  {
    //lang=postgresql
    const string COMMAND_TEXT = """
      WITH next_job AS (
          UPDATE background_jobs
          SET
              "attempt" = "attempt" + 1,
              "status" = @Status1,
              "updatedAt" = NOW()
          WHERE id = (
              SELECT id FROM background_jobs
              WHERE ( -- job in a QUEUED state which has not yet exceeded maximum attempts and has a positive remaining compute budget
                  (payload ->> 'fileType') = ANY(@FileTypes)
                  AND status = @Status2
                  AND "attempt" < "maxAttempt"
                  AND "remainingComputeBudgetSeconds"::int > 0
              )
              OR ( -- any job left in a PROCESSING state for more than its timeout period
                  (payload ->> 'fileType') = ANY(@FileTypes)
                  AND status = @Status1
                  AND "attempt" <= "maxAttempt"
                  AND "updatedAt" < NOW() - (payload ->> 'timeOutSeconds')::int * interval '1 second'
              )
              ORDER BY "createdAt"
              FOR UPDATE SKIP LOCKED
              LIMIT 1
          )
          RETURNING *
      )
      SELECT * FROM next_job;
      """;

    var command = new CommandDefinition(
      commandText: COMMAND_TEXT,
      parameters: new
      {
        Status1 = nameof(JobStatus.PROCESSING).ToLowerInvariant(),
        Status2 = nameof(JobStatus.QUEUED).ToLowerInvariant(),
        FileTypes = SupportedFileTypes.FileTypes,
      },
      cancellationToken: cancellationToken
    );

    return await connection.QueryFirstOrDefaultAsync<FileimportJob?>(command);
  }

  public async Task DeductFromComputeBudget(
    IDbConnection connection,
    string jobId,
    long usedComputeTimeSeconds,
    CancellationToken cancellationToken
  )
  {
    logger.LogInformation(
      "updating job: {jobId}'s remaining compute budget by deducting {usedComputeTimeSeconds} seconds",
      jobId,
      usedComputeTimeSeconds
    );

    //lang=postgresql
    const string COMMAND_TEXT = """
      UPDATE background_jobs
      SET "remainingComputeBudgetSeconds" = "remainingComputeBudgetSeconds"::int - @usedComputeTimeSeconds,
          "updatedAt" = NOW()
      WHERE id = @jobId
      """;

    var command = new CommandDefinition(
      commandText: COMMAND_TEXT,
      parameters: new { usedComputeTimeSeconds, jobId },
      cancellationToken: cancellationToken
    );

    await connection.ExecuteAsync(command);
  }
}
