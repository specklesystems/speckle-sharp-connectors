using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Sdk.Api;
using Speckle.Sdk.Logging;

namespace Speckle.Importers.JobProcessor.Blobs;

internal sealed class ImportJobFileDownloader(ILogger<ImportJobFile> logger, ISdkActivityFactory activityFactory)
{
  public async Task<ImportJobFile> DownloadFile(FileimportJob job, IClient client, CancellationToken cancellationToken)
  {
    using var activity = activityFactory.Start();
    try
    {
      var directory = Directory.CreateTempSubdirectory("speckle-file-import");
      string targetFilePath = $"{directory.FullName}/{job.Payload.BlobId}.{job.Payload.FileType}";
      await client.FileImport.DownloadFile(
        job.Payload.ProjectId,
        job.Payload.BlobId,
        targetFilePath,
        null,
        cancellationToken
      );
      var ret = new ImportJobFile(logger, new FileInfo(targetFilePath));
      activity?.SetStatus(SdkActivityStatusCode.Ok);
      return ret;
    }
    catch (Exception ex)
    {
      activity?.RecordException(ex);
      activity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
  }
}
