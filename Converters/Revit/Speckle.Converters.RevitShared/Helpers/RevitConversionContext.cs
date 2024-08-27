using Speckle.Converters.Common;

namespace Speckle.Converters.RevitShared.Helpers;

public record RevitConversionContext : IRevitConversionContext
{
  private IRevitConversionContext _document;

  public RevitConversionContext(
    DB.Document document,
    string speckleUnits,
    RevitRenderMaterialProxyCacheSingleton renderMaterialProxyCache
  )
  {
    Document = document;
    SpeckleUnits = speckleUnits;
    RenderMaterialProxyCache = renderMaterialProxyCache;
  }

  public double Tolerance { get; } = 0.01;
  public DB.Document Document { get; }

  public string SpeckleUnits { get; }
  public RevitRenderMaterialProxyCacheSingleton RenderMaterialProxyCache { get; }

  public IRevitConversionContext Duplicate()
  {
    return new RevitConversionContext(Document, SpeckleUnits, RenderMaterialProxyCache);
  }
}
