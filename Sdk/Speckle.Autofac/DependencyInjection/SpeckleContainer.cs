using Autofac;

namespace Speckle.Autofac.DependencyInjection;

public sealed class SpeckleContainer(IContainer container) : IDisposable
{
  public T Resolve<T>()
    where T : class => container.Resolve<T>();

  public void Dispose() => container.Dispose();
}
