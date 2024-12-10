using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.Grasshopper8.Components.BaseComponents;

public abstract class SpeckleScopedTaskCapableComponent<TInput, TOutput>(
  string name,
  string nickname,
  string description,
  string category,
  string subCategory
) : SpeckleTaskCapableComponent<TInput, TOutput>(name, nickname, description, category, subCategory)
{
  protected override async Task<TOutput> PerformTask(TInput input, CancellationToken cancellationToken = default)
  {
    using var scope = PriorityLoader.Container.CreateScope();
    return await PerformScopedTask(input, scope, cancellationToken).ConfigureAwait(false);
  }

  protected abstract Task<TOutput> PerformScopedTask(
    TInput input,
    IServiceScope scope,
    CancellationToken cancellationToken = default
  );
}
