using Microsoft.Extensions.Logging;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstanceFactory(Sender sender, ILogger<ImporterInstance> logger)
{
  public ImporterInstance Create(ImporterArgs args) => new(args, sender, logger);
}
