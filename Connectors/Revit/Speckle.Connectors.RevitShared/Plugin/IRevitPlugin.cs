using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.Revit.Plugin;

internal interface IRevitPlugin : IRevitContext
{
  void Initialise();
  void Shutdown();
}


