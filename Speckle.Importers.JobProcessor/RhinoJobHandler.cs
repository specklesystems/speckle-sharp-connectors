using System.Diagnostics;
using Speckle.Importers.JobProcessor.Domain;

namespace Speckle.Importers.JobProcessor;

public sealed class RhinoJobHandler : IJobHandler
{
  public async Task ProcessJob(FileimportJob job, CancellationToken cancellationToken)
  {
    var process = Process.Start("", []);

    cancellationToken.Register(() =>
    {
      process.Kill();
    });

    await process.WaitForExitAsync(CancellationToken.None);
    if (process.ExitCode != 0)
    {
      //attempt to read json result, throw an exception with it
    }
  }
}
