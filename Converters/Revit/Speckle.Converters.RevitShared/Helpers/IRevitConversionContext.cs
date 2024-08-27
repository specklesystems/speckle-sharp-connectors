using Speckle.Converters.Common;

namespace Speckle.Converters.RevitShared.Helpers;

public interface IRevitConversionContext : IConversionContext<IRevitConversionContext>
{
  double Tolerance { get; }
  DB.Document Document { get; }
  string SpeckleUnits { get; }
  RevitRenderMaterialProxyCacheSingleton RenderMaterialProxyCache { get; }
}
