using Autofac;

namespace Speckle.Autofac.DependencyInjection;

public interface IUnitOfWork : IDisposable
{
  T Resolve<T>()
    where T : class;
}

public sealed class UnitOfWork(ILifetimeScope unitOfWorkScope) : IUnitOfWork
{
  private bool _notDisposed = true;

  public T Resolve<T>()
    where T : class => unitOfWorkScope.Resolve<T>();

  public void Dispose()
  {
    if (_notDisposed)
    {
      unitOfWorkScope.Dispose();
      _notDisposed = false;
    }
  }
}
