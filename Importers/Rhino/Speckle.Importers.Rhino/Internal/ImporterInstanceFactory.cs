using Microsoft.Extensions.Logging;
using Speckle.Sdk.Api;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstanceFactory(
  Sender sender,
  IClientFactory clientFactory,
  ILogger<ImporterInstance> logger
)
{
  public ImporterInstance Create(ImporterArgs args)
  {
    var speckleClient = clientFactory.Create(args.Account);
    return new(args, sender, speckleClient, logger);
  }
}
