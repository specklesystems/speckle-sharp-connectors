using Microsoft.Extensions.DependencyInjection;
using Speckle.Converters.CSiShared.ToSpeckle.Helpers;
using Speckle.Converters.CSiShared.Utils;

namespace Speckle.Connectors.CSiShared.HostApp.Helpers;

public class CsiResultsExtractorFactory
{
  private readonly IServiceProvider _serviceProvider;

  public CsiResultsExtractorFactory(IServiceProvider serviceProvider)
  {
    _serviceProvider = serviceProvider;
  }

  public IApplicationResultsExtractor GetExtractor(string resultsKey) =>
    resultsKey switch
    {
      ResultsKey.BASE_REACT => _serviceProvider.GetRequiredService<CsiBaseReactResultsExtractor>(),
      ResultsKey.FRAME_FORCES => _serviceProvider.GetRequiredService<CsiFrameForceResultsExtractor>(),
      ResultsKey.JOINT_REACT => _serviceProvider.GetRequiredService<CsiJointReactResultsExtractor>(),
      ResultsKey.MODAL_PERIOD => _serviceProvider.GetRequiredService<CsiModalPeriodExtractor>(),
      ResultsKey.PIER_FORCES => _serviceProvider.GetRequiredService<CsiPierForceResultsExtractor>(),
      ResultsKey.SPANDREL_FORCES => _serviceProvider.GetRequiredService<CsiSpandrelForceResultsExtractor>(),
      ResultsKey.STORY_DRIFTS => _serviceProvider.GetRequiredService<CsiStoryDriftsResultsExtractor>(),
      ResultsKey.STORY_FORCES => _serviceProvider.GetRequiredService<CsiStoryForceResultsExtractor>(),
      _ => throw new InvalidOperationException($"{resultsKey} not accounted for in CsiResultsExtractorFactory"),
    };
}
