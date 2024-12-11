using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Speckle.HostApps;

public sealed class SpeckleXunitTestFrameworkExecutor(
  IServiceProvider serviceProvider,
  AssemblyName assemblyName,
  ISourceInformationProvider sourceInformationProvider,
  IMessageSink diagnosticMessageSink)
  : XunitTestFrameworkExecutor(assemblyName, sourceInformationProvider, diagnosticMessageSink)
{
  internal ExceptionAggregator Aggregator { get; set; } = new ExceptionAggregator();

  /// <inheritdoc />
  protected override async void RunTestCases(
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink executionMessageSink,
    ITestFrameworkExecutionOptions executionOptions)
  {
    using var runner = new SpeckleXunitTestAssemblyRunner(serviceProvider, TestAssembly, testCases, DiagnosticMessageSink,
      executionMessageSink, executionOptions, Aggregator);
    await  runner.RunAsync().ConfigureAwait(false);
  }
}
