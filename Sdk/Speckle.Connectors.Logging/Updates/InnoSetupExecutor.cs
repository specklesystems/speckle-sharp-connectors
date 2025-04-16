using Microsoft.Extensions.Logging;
using Onova.Services;

namespace Speckle.Connectors.Logging.Updates;

public class InnoSetupExecutor(string name, ILogger<InnoSetupExecutor> logger) : IPackageExtractor
{
  public Task ExtractPackageAsync(
    string sourceFilePath,
    string destDirPath,
    IProgress<double>? progress = null,
    CancellationToken cancellationToken = default
  )
  {
    logger.LogInformation($"Running inno setup package...Source: '{sourceFilePath}' Destination: '{destDirPath}'");
    File.Copy(sourceFilePath, Path.Combine(destDirPath, $"{name}.Updater.exe"), true);
    return Task.CompletedTask;
  }
}
