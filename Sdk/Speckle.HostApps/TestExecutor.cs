using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Runners;

namespace Speckle.HostApps;

public sealed class TestExecutor(Assembly assembly) : IMessageSinkWithTypes
{
  public Action<DiagnosticMessageInfo> OnDiagnosticMessage { get; set; }

  public Action<IDiscoveryCompleteMessage>? OnDiscoveryComplete { get; set; }
  public Action<ITestCaseDiscoveryMessage>? OnDiscoveryMessage { get; set; }

  public Action<ErrorMessageInfo>? OnErrorMessage { get; set; }

  public Action<ExecutionCompleteInfo>? OnExecutionComplete { get; set; }

  public Action<TestFailedInfo>? OnTestFailed { get; set; }

  public Action<TestFinishedInfo>? OnTestFinished { get; set; }

  public Action<TestOutputInfo>? OnTestOutput { get; set; }

  public Action<TestPassedInfo>? OnTestPassed { get; set; }

  public Action<TestSkippedInfo>? OnTestSkipped { get; set; }

  public Action<TestStartingInfo>? OnTestStarting { get; set; }
  public AssemblyRunnerStatus Status
  {
    get
    {
      if (!_discoveryCompleteEvent.WaitOne(0))
      {
        return AssemblyRunnerStatus.Discovering;
      }

      if (!_executionCompleteEvent.WaitOne(0))
      {
        return AssemblyRunnerStatus.Executing;
      }

      return AssemblyRunnerStatus.Idle;
    }
  }

  private bool _disposed;

  private readonly ManualResetEvent _discoveryCompleteEvent = new ManualResetEvent(true);
  private readonly ManualResetEvent _executionCompleteEvent = new ManualResetEvent(true);
  private readonly object _statusLock = new object();
  private int _testCasesDiscovered;
  private volatile bool _cancelled;

  public void Cancel()
  {
    _cancelled = true;
  }

  public void Dispose()
  {
    lock (_statusLock)
    {
      if (_disposed)
      {
        return;
      }

      if (Status != AssemblyRunnerStatus.Idle)
#pragma warning disable CA1065
      {
        throw new InvalidOperationException("Cannot dispose the assembly runner when it's not idle");
      }
#pragma warning restore CA1065

      _disposed = true;
    }

    _discoveryCompleteEvent.Dispose();
    _executionCompleteEvent.Dispose();
  }

  public void StartFind()
  {
    using XunitFrontController controller = new(AppDomainSupport.Denied, assembly.Location);
    _discoveryCompleteEvent.Reset();
    ITestFrameworkDiscoveryOptions discoveryOptions = TestFrameworkOptions.ForDiscovery();
    controller.Find(false, this, discoveryOptions);
  }

  public void WaitForFindFinish()
  {
    _discoveryCompleteEvent.WaitOne();
  }

  public void StartExecution()
  {
    using XunitFrontController controller = new(AppDomainSupport.Denied, assembly.Location);
    _executionCompleteEvent.Reset();
    ITestFrameworkExecutionOptions executionOptions = TestFrameworkOptions.ForExecution();
    ITestFrameworkDiscoveryOptions discoveryOptions = TestFrameworkOptions.ForDiscovery();
    controller.RunAll(this, discoveryOptions, executionOptions);
  }

  public void WaitForExecutionFinish()
  {
    _executionCompleteEvent.WaitOne();
  }

  private bool DispatchMessage<TMessage>(
    IMessageSinkMessage message,
    HashSet<string> messageTypes,
    Action<TMessage> handler
  )
    where TMessage : class
  {
    if (!messageTypes.Contains(typeof(TMessage).FullName ?? throw new InvalidOperationException()))
    {
      return false;
    }

    handler((TMessage)message);
    return true;
  }

#pragma warning disable CA1502
  bool IMessageSinkWithTypes.OnMessageWithTypes(IMessageSinkMessage message, HashSet<string> messageTypes)
  {
    if (
      DispatchMessage<ITestCaseDiscoveryMessage>(
        message,
        messageTypes,
        testDiscovered =>
        {
          OnDiscoveryMessage?.Invoke(testDiscovered);
          ++_testCasesDiscovered;
        }
      )
    )
    {
      return !_cancelled;
    }

#pragma warning restore CA1502
    if (
      DispatchMessage<IDiscoveryCompleteMessage>(
        message,
        messageTypes,
        discoveryComplete =>
        {
          OnDiscoveryComplete?.Invoke(discoveryComplete);
          _discoveryCompleteEvent.Set();
        }
      )
    )
    {
      return !_cancelled;
    }

    if (
      DispatchMessage<ITestAssemblyFinished>(
        message,
        messageTypes,
        assemblyFinished =>
        {
          OnExecutionComplete?.Invoke(
            new ExecutionCompleteInfo(
              assemblyFinished.TestsRun,
              assemblyFinished.TestsFailed,
              assemblyFinished.TestsSkipped,
              assemblyFinished.ExecutionTime
            )
          );
          _executionCompleteEvent.Set();
        }
      )
    )
    {
      return !_cancelled;
    }

    if (OnDiagnosticMessage != null)
    {
      if (
        DispatchMessage<IDiagnosticMessage>(
          message,
          messageTypes,
          m => OnDiagnosticMessage(new DiagnosticMessageInfo(m.Message))
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnTestFailed != null)
    {
      if (
        DispatchMessage<ITestFailed>(
          message,
          messageTypes,
          m =>
            OnTestFailed(
              new TestFailedInfo(
                m.TestClass.Class.Name,
                m.TestMethod.Method.Name,
                m.TestCase.Traits,
                m.Test.DisplayName,
                m.TestCollection.DisplayName,
                m.ExecutionTime,
                m.Output,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnTestFinished != null)
    {
      if (
        DispatchMessage<ITestFinished>(
          message,
          messageTypes,
          m =>
            OnTestFinished(
              new TestFinishedInfo(
                m.TestClass.Class.Name,
                m.TestMethod.Method.Name,
                m.TestCase.Traits,
                m.Test.DisplayName,
                m.TestCollection.DisplayName,
                m.ExecutionTime,
                m.Output
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnTestOutput != null)
    {
      if (
        DispatchMessage<ITestOutput>(
          message,
          messageTypes,
          m =>
            OnTestOutput(
              new TestOutputInfo(
                m.TestClass.Class.Name,
                m.TestMethod.Method.Name,
                m.TestCase.Traits,
                m.Test.DisplayName,
                m.TestCollection.DisplayName,
                m.Output
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnTestPassed != null)
    {
      if (
        DispatchMessage<ITestPassed>(
          message,
          messageTypes,
          m =>
            OnTestPassed(
              new TestPassedInfo(
                m.TestClass.Class.Name,
                m.TestMethod.Method.Name,
                m.TestCase.Traits,
                m.Test.DisplayName,
                m.TestCollection.DisplayName,
                m.ExecutionTime,
                m.Output
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnTestSkipped != null)
    {
      if (
        DispatchMessage<ITestSkipped>(
          message,
          messageTypes,
          m =>
            OnTestSkipped(
              new TestSkippedInfo(
                m.TestClass.Class.Name,
                m.TestMethod.Method.Name,
                m.TestCase.Traits,
                m.Test.DisplayName,
                m.TestCollection.DisplayName,
                m.Reason
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnTestStarting != null)
    {
      if (
        DispatchMessage<ITestStarting>(
          message,
          messageTypes,
          m =>
            OnTestStarting(
              new TestStartingInfo(
                m.TestClass.Class.Name,
                m.TestMethod.Method.Name,
                m.TestCase.Traits,
                m.Test.DisplayName,
                m.TestCollection.DisplayName
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    if (OnErrorMessage != null)
    {
      if (
        DispatchMessage<IErrorMessage>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.CatastrophicError,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }

      if (
        DispatchMessage<ITestAssemblyCleanupFailure>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.TestAssemblyCleanupFailure,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }

      if (
        DispatchMessage<ITestCaseCleanupFailure>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.TestCaseCleanupFailure,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }

      if (
        DispatchMessage<ITestClassCleanupFailure>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.TestClassCleanupFailure,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }

      if (
        DispatchMessage<ITestCleanupFailure>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.TestCleanupFailure,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }

      if (
        DispatchMessage<ITestCollectionCleanupFailure>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.TestCollectionCleanupFailure,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }

      if (
        DispatchMessage<ITestMethodCleanupFailure>(
          message,
          messageTypes,
          m =>
            OnErrorMessage(
              new ErrorMessageInfo(
                ErrorMessageType.TestMethodCleanupFailure,
                m.ExceptionTypes.FirstOrDefault(),
                m.Messages.FirstOrDefault(),
                m.StackTraces.FirstOrDefault()
              )
            )
        )
      )
      {
        return !_cancelled;
      }
    }

    return !_cancelled;
  }
}
