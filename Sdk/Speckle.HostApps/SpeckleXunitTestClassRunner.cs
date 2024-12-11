using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Speckle.HostApps;

public class SpeckleXunitTestClassRunner : XunitTestClassRunner
{
  private readonly IServiceScope _serviceScope;

  public SpeckleXunitTestClassRunner(IServiceScope serviceScope,
    ITestClass testClass,
    IReflectionTypeInfo @class,
    IEnumerable<IXunitTestCase> testCases,
    IMessageSink diagnosticMessageSink,
    IMessageBus messageBus,
    ITestCaseOrderer testCaseOrderer,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource,
    IDictionary<Type, object> collectionFixtureMappings)
    : base(testClass, @class, testCases, diagnosticMessageSink,
      messageBus, testCaseOrderer, aggregator,
      cancellationTokenSource, collectionFixtureMappings) => 
    _serviceScope = serviceScope;

  /// <inheritdoc />
  protected override object[] CreateTestClassConstructorArguments()
  {
    if (Class.Type.GetTypeInfo().IsAbstract && Class.Type.GetTypeInfo().IsSealed)
    {
      return [];
    }

    var constructor = SelectTestClassConstructor();
    if (constructor == null)
    {
      return [];
    }

    var parameters = constructor.GetParameters();

    var parameterValues = new object[parameters.Length];
    for (var i = 0; i < parameters.Length; ++i)
    {
      var parameterInfo = parameters[i];
      if (TryGetConstructorArgument(constructor, i, parameterInfo, out var parameterValue))
      {
        parameterValues[i] = parameterValue;
      }
      else
      {
        try
        {
          parameterValues[i] = _serviceScope.ServiceProvider.GetRequiredService(parameterInfo.ParameterType);
        }
#pragma warning disable CA1031
        catch (Exception exception)
#pragma warning restore CA1031
        {
          Aggregator.Add(exception);
        }
      }
    }

    return parameterValues;
  }
}
