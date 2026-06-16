using Microsoft.Extensions.DependencyInjection;

namespace Speckle.Connectors.TSDShared.Operations.Send.Results;

internal sealed class TsdResultsExtractorFactory
{
  private readonly IServiceProvider _serviceProvider;

  public TsdResultsExtractorFactory(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public ITsdResultsExtractor GetExtractor(string resultsKey) =>
    resultsKey switch
    {
      TsdResultsKeys.ELEMENT_END_FORCES => _serviceProvider.GetRequiredService<TsdElementEndForceResultsExtractor>(),
      TsdResultsKeys.OFFSET_FORCES => _serviceProvider.GetRequiredService<TsdOffsetForceResultsExtractor>(),
      TsdResultsKeys.REACTIONS => _serviceProvider.GetRequiredService<TsdReactionResultsExtractor>(),
      TsdResultsKeys.SLAB_FORCES => _serviceProvider.GetRequiredService<TsdSlabForceResultsExtractor>(),
      TsdResultsKeys.WALL_FORCES => _serviceProvider.GetRequiredService<TsdWallForceResultsExtractor>(),
      TsdResultsKeys.NODAL_DISPLACEMENTS => _serviceProvider.GetRequiredService<TsdNodalDisplacementResultsExtractor>(),
      TsdResultsKeys.OFFSET_DISPLACEMENTS =>
        _serviceProvider.GetRequiredService<TsdOffsetDisplacementResultsExtractor>(),
      TsdResultsKeys.RESULT_LINE_FORCES => _serviceProvider.GetRequiredService<TsdResultLineForceResultsExtractor>(),
      TsdResultsKeys.WALL_LINE_FORCES => _serviceProvider.GetRequiredService<TsdWallLineForceResultsExtractor>(),
      _ => throw new InvalidOperationException($"{resultsKey} not accounted for in TsdResultsExtractorFactory"),
    };
}
