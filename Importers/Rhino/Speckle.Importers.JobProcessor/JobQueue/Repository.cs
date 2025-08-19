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
              WHERE ( --queued job
                  (payload ->> 'fileType') = ANY(@FileTypes)
                  AND status = @Status2
              )
              OR ( --timed job left on processing state
                  (payload ->> 'fileType') = ANY(@FileTypes)
                  AND status = @Status1
                  AND "updatedAt" < NOW() - ("timeoutMs" * interval '1 millisecond')
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

  public async Task SetJobStatus(
    IDbConnection connection,
    string jobId,
    JobStatus jobStatus,
    CancellationToken cancellationToken
  )
  {
    logger.LogInformation("Updating job: {jobId}'s status to {jobStatus}", jobId, jobStatus);

    //lang=postgresql
    const string COMMAND_TEXT = """
      UPDATE background_jobs
      SET status = @status, "updatedAt" = NOW()
      WHERE id = @jobId
      """;

    var command = new CommandDefinition(
      commandText: COMMAND_TEXT,
      parameters: new { status = jobStatus.ToString().ToLowerInvariant(), jobId, },
      cancellationToken: cancellationToken
    );

    await connection.ExecuteAsync(command);
  }
}
