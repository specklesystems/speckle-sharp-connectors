using Autofac;
using Autofac.Core;

namespace Speckle.Autofac.DependencyInjection;

public interface IUnitOfWorkFactory
{
  public IUnitOfWork Create();
}

public class UnitOfWorkFactory(ILifetimeScope parentScope) : IUnitOfWorkFactory
{
  public IUnitOfWork Create()
  {
    ILifetimeScope? childScope = null;

    try
    {
      childScope = parentScope.BeginLifetimeScope();
      return new UnitOfWork(childScope);
    }
    catch (DependencyResolutionException)
    {
      childScope?.Dispose();
      throw;
    }
  }
}
