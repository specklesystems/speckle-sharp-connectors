using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using Speckle.Importers.JobProcessor.Domain;

namespace Speckle.Importers.JobProcessor;

public sealed class Repository(ILogger<Repository> logger)
{
  public async Task<NpgsqlConnection> SetupConnection(CancellationToken cancellationToken)
  {
    string fileImportQueuePostgresUrl =
      Environment.GetEnvironmentVariable("FILEIMPORT_QUEUE_POSTGRES_URL")
      ?? throw new ArgumentException("Expected FILEIMPORT_QUEUE_POSTGRES_URL environment variable to be set");

    string connectionString = ParseConnectionString(new(fileImportQueuePostgresUrl));
    var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync(cancellationToken);
    return connection;
  }

  private string ParseConnectionString(Uri connectionUrl)
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

  public async Task<FileimportJob?> GetNextJob(
    IDbConnection connection,
    IDbTransaction transaction,
    CancellationToken cancellationToken
  )
  {
    //lang=postgresql
    const string COMMAND_TEXT = """
      SELECT * FROM background_jobs
      WHERE payload ->> 'fileType' = 'obj' AND status = @status AND attempt < "maxAttempt"
      ORDER BY "createdAt"
      FOR UPDATE SKIP LOCKED
      LIMIT 1
      """;

    var command = new CommandDefinition(
      commandText: COMMAND_TEXT,
      parameters: new { Status = nameof(JobStatus.QUEUED).ToLowerInvariant() },
      transaction: transaction,
      cancellationToken: cancellationToken
    );

    return await connection.QueryFirstOrDefaultAsync<FileimportJob?>(command);
  }

  public async Task SetJobStatus(
    IDbConnection connection,
    IDbTransaction? transaction,
    string jobId,
    JobStatus jobStatus,
    int attempt,
    CancellationToken cancellationToken
  )
  {
    logger.LogInformation(
      "Updating job: {jobId}'s status to {jobStatus}, with attempt: {attempt}",
      jobId,
      jobStatus,
      attempt
    );

    //lang=postgresql
    const string COMMAND_TEXT = """
      UPDATE background_jobs
      SET status = @status, "updatedAt" = NOW(), attempt = @attempt
      WHERE id = @jobId
      """;

    var command = new CommandDefinition(
      commandText: COMMAND_TEXT,
      parameters: new
      {
        status = jobStatus.ToString().ToLowerInvariant(),
        attempt,
        jobId,
      },
      transaction: transaction,
      cancellationToken: cancellationToken
    );

    await connection.ExecuteAsync(command);
  }
}
