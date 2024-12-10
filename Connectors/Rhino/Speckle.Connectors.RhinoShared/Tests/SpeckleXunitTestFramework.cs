using System.Reflection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Speckle.Converters.Rhino7.Tests;

public sealed class SpeckleXunitTestFramework(IMessageSink messageSink) : XunitTestFramework(messageSink)
{
  private ExceptionAggregator Aggregator { get; set; } = new ExceptionAggregator();
  
  public static IServiceProvider? ServiceProvider { get; set; }
        
  /// <inheritdoc/>
  protected override ITestFrameworkExecutor CreateExecutor(AssemblyName assemblyName)
  {
    try
    {
      if (ServiceProvider != null)
      {
        return new SpeckleXunitTestFrameworkExecutor(
          ServiceProvider, assemblyName, SourceInformationProvider, DiagnosticMessageSink);
      }
    }
#pragma warning disable CA1031
    catch (Exception exception)
#pragma warning restore CA1031
    {
      Aggregator.Add(exception);
    }

    return base.CreateExecutor(assemblyName);
  }
}
