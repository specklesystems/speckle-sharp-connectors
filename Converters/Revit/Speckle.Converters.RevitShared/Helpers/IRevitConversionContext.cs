namespace Speckle.Converters.RevitShared.Helpers;

public interface IRevitConversionContext
{
  double Tolerance { get; }
  DB.Document Document { get; }
  string SpeckleUnits { get; }
  RevitRenderMaterialProxyCacheSingleton RenderMaterialProxyCache { get; }
}
