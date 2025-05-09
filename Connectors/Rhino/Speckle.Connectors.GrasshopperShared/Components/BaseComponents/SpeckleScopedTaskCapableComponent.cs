using Microsoft.Extensions.DependencyInjection;
using Speckle.Connectors.GrasshopperShared.Registration;

namespace Speckle.Connectors.GrasshopperShared.Components.BaseComponents;

public abstract class SpeckleScopedTaskCapableComponent<TInput, TOutput>(
  string name,
  string nickname,
  string description,
  string category,
  string subCategory
) : SpeckleTaskCapableComponent<TInput, TOutput>(name, nickname, description, category, subCategory)
{
  protected override Task<TOutput> PerformTask(TInput input, CancellationToken cancellationToken = default)
  {
    /*using*/var scope = PriorityLoader.Container.CreateScope(); // NOTE: this component does not work as intended in e.g the receive component; the scope gets disposed before the task completes.
    return PerformScopedTask(input, scope, cancellationToken);
  }

  protected abstract Task<TOutput> PerformScopedTask(
    TInput input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  );
}
