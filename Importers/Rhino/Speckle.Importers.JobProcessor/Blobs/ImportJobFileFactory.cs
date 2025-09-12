using Microsoft.Extensions.Logging;
using Speckle.Importers.JobProcessor.Domain;
using Speckle.Sdk.Api;

namespace Speckle.Importers.JobProcessor.Blobs;

internal sealed class ImportJobFileDownloader(ILogger<ImportJobFile> logger)
{
  public async Task<ImportJobFile> DownloadFile(FileimportJob job, IClient client, CancellationToken cancellationToken)
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
    return new ImportJobFile(logger, new FileInfo(targetFilePath));
  }
}
