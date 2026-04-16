using Speckle.Sdk.Api;
using Speckle.Sdk.Logging;

namespace Speckle.Importers.Rhino.Internal;

internal sealed class ImporterInstanceFactory(
  Sender sender,
  IClientFactory clientFactory,
  ISdkActivityFactory activityFactory
)
{
  public ImporterInstance Create(ImporterArgs args)
  {
    var speckleClient = clientFactory.Create(args.Account);
    return new(args, sender, speckleClient, activityFactory);
  }
}
