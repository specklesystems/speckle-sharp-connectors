using Xunit.Abstractions;
using Xunit.Sdk;

namespace Speckle.HostApps;

public class SpeckleXunitTestAssemblyRunner : XunitTestAssemblyRunner
{
  private readonly IServiceProvider _provider;

  public SpeckleXunitTestAssemblyRunner(IServiceProvider provider,
    ITestAssembly testAssembly,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageSink executionMessageSink,
    ITestFrameworkExecutionOptions executionOptions,
    ExceptionAggregator exceptions)
    : base(testAssembly, testCases, diagnosticMessageSink,
      executionMessageSink, executionOptions)
  {
    _provider = provider;
    Aggregator.Aggregate(exceptions);
  }

  /// <inheritdoc />
  protected override Task<RunSummary> RunTestCollectionAsync(IMessageBus messageBus,
    ITestCollection testCollection,
    IEnumerable<IXunitTestCase> testCases,
    CancellationTokenSource cancellationTokenSource)
  {
    return new SpeckleXunitTestCollectionRunner(_provider, testCollection,
        testCases, DiagnosticMessageSink, messageBus, TestCaseOrderer,
        new ExceptionAggregator(Aggregator), cancellationTokenSource)
      .RunAsync();
  }
}
