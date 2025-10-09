using Microsoft.Extensions.Logging;

namespace Speckle.Importers.JobProcessor.Blobs;

/// <summary>
/// <see cref="IDisposable"/> wrapper around the downloaded file
/// </summary>
/// <remarks>
/// This is an attempt to use a disposal pattern to ensure that downloaded file is deleted after its use.
/// </remarks>
/// <param name="logger"></param>
/// <param name="file"></param>
internal sealed class ImportJobFile(ILogger<ImportJobFile> logger, FileInfo file) : IDisposable
{
  public FileInfo FileInfo => file;

  private void DeleteFile()
  {
    var dir = file.Directory;
    try
    {
      file.Delete();
    }
    finally
    {
      dir?.Delete(true);
    }
  }

  public void Dispose()
  {
    try
    {
      DeleteFile();
      GC.SuppressFinalize(this);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "Failed to cleanup file");

      // We had hoped that making Rhino a sub-process would help avoid this scenario, and it mostly has,
      // But occasionally, we still see some particually weird 3dm files staying locked even after the process has exited...
      // For now, we'll just swallow the IOException
      if (ex is not IOException)
      {
        throw;
      }
    }
  }

  ~ImportJobFile()
  {
    // Using the full unmanaged style disposal pattern means that the file will be deleted on finalise if dispose wasn't called already.
    DeleteFile();
  }
}
