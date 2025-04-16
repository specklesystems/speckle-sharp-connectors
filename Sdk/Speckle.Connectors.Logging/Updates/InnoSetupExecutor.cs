using Onova.Services;

namespace Speckle.Connectors.Logging.Updates;

public class InnoSetupExecutor : IPackageExtractor
{
  public  Task ExtractPackageAsync(string sourceFilePath, string destDirPath, IProgress<double>? progress = null,
    CancellationToken cancellationToken = new CancellationToken()) =>
    throw new NotImplementedException();
}
