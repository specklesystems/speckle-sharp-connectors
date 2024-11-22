using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Connectors.DUI.Utils;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.DUI.Bridge;

/// <summary>
/// Wraps a binding class, and manages its calls from the Frontend to .NET, and sending events from .NET to the the Frontend.
/// <para>Initially inspired by: https://github.com/johot/WebView2-better-bridge</para>
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public sealed class BrowserBridge : IBrowserBridge
{
  /// <summary>
  /// The name under which we expect the frontend to hoist this bindings class to the global scope.
  /// e.g., `receiveBindings` should be available as `window.receiveBindings`.
  /// </summary>

  private readonly ConcurrentDictionary<string, string?> _resultsStore = new();
  private readonly SynchronizationContext _mainThreadContext;
  public ITopLevelExceptionHandler TopLevelExceptionHandler { get; }

  private readonly IBrowserScriptExecutor _browserScriptExecutor;
  private readonly IJsonSerializer _jsonSerializer;

  private IReadOnlyDictionary<string, MethodInfo> _bindingMethodCache = new Dictionary<string, MethodInfo>();
  private IBinding? _binding;
  private Type? _bindingType;

  private readonly ILogger _logger;

  public string FrontendBoundName { get; private set; } = "Unknown";

  public IBinding? Binding
  {
    get => _binding;
    private set
    {
      if (_binding != null || this != value?.Parent)
      {
        throw new ArgumentException($"Binding: {FrontendBoundName} is already bound or does not match bridge");
      }

      _binding = value;
    }
  }

  private struct RunMethodArgs
  {
    public string MethodName;
    public string RequestId;
    public string MethodArgs;
  }

  public BrowserBridge(
    IJsonSerializer jsonSerializer,
    ILogger<BrowserBridge> logger,
    ILogger<TopLevelExceptionHandler> topLogger,
    IBrowserScriptExecutor browserScriptExecutor
  )
  {
    _jsonSerializer = jsonSerializer;
    _logger = logger;
    TopLevelExceptionHandler = new TopLevelExceptionHandler(topLogger, this);
    // Capture the main thread's SynchronizationContext
    _mainThreadContext = SynchronizationContext.Current.NotNull("No UI thread to capture?");
    _browserScriptExecutor = browserScriptExecutor;
  }

  public void AssociateWithBinding(IBinding binding)
  {
    // set via binding property to ensure explosion if already bound
    Binding = binding;
    FrontendBoundName = binding.Name;

    _bindingType = binding.GetType();

    // Note: we need to filter out getter and setter methods here because they are not really nicely
    // supported across browsers, hence the !method.IsSpecialName.
    var bindingMethodCache = new Dictionary<string, MethodInfo>();
    foreach (var m in _bindingType.GetMethods().Where(method => !method.IsSpecialName))
    {
      bindingMethodCache[m.Name] = m;
    }
    _bindingMethodCache = bindingMethodCache;
    _logger.LogInformation("Bridge bound to front end name {FrontEndName}", binding.Name);
  }

  private async Task OnActionBlock(RunMethodArgs args)
  {
    Result<object?> result = await TopLevelExceptionHandler
      .CatchUnhandledAsync(async () => await ExecuteMethod(args.MethodName, args.MethodArgs).ConfigureAwait(false))
      .ConfigureAwait(false);

    string resultJson = result.IsSuccess
      ? _jsonSerializer.Serialize(result.Value)
      : SerializeFormattedException(result.Exception);

    await NotifyUIMethodCallResultReady(args.RequestId, resultJson).ConfigureAwait(false);
  }

  /// <summary>
  /// Used by the Frontend bridge logic to understand which methods are available.
  /// </summary>
  /// <returns></returns>
  public string[] GetBindingsMethodNames()
  {
    var bindingNames = _bindingMethodCache.Keys.ToArray();
    Debug.WriteLine($"{FrontendBoundName}: " + JsonConvert.SerializeObject(bindingNames, Formatting.Indented));
    return bindingNames;
  }

  /// <summary>
  /// This method posts the requested call to our action block executor.
  /// </summary>
  /// <param name="methodName"></param>
  /// <param name="requestId"></param>
  /// <param name="args"></param>
  public void RunMethod(string methodName, string requestId, string args) =>
    _mainThreadContext.Post(
      async x =>
      {
        var runMethodArgs = (RunMethodArgs)x;
        var task = await TopLevelExceptionHandler
          .CatchUnhandledAsync(async () =>
          {
            var result = await ExecuteMethod(runMethodArgs.MethodName, runMethodArgs.MethodArgs).ConfigureAwait(false);
            string resultJson = _jsonSerializer.Serialize(result);
            await NotifyUIMethodCallResultReady(runMethodArgs.RequestId, resultJson).ConfigureAwait(false);
          })
          .ConfigureAwait(false);
        if (task.Exception is not null)
        {
          string resultJson = SerializeFormattedException(task.Exception);
          await NotifyUIMethodCallResultReady(runMethodArgs.RequestId, resultJson).ConfigureAwait(false);
        }
      },
      new RunMethodArgs
      {
        MethodName = methodName,
        RequestId = requestId,
        MethodArgs = args
      }
    );

  public void RunOnMainThread(Action action) =>
    _mainThreadContext.Post(
      _ =>
      {
        // Execute the action on the main thread
        TopLevelExceptionHandler.CatchUnhandled(action);
      },
      null
    );

  public async Task RunOnMainThreadAsync(Func<Task> action) =>
    await RunOnMainThreadAsync<object?>(async () =>
      {
        await action.Invoke().ConfigureAwait(false);
        return null;
      })
      .ConfigureAwait(false);

  [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "TaskCompletionSource")]
  public Task<T> RunOnMainThreadAsync<T>(Func<Task<T>> action)
  {
    TaskCompletionSource<T> tcs = new();

    _mainThreadContext.Post(
      async _ =>
      {
        try
        {
          T result = await action.Invoke().ConfigureAwait(false);
          tcs.SetResult(result);
        }
        catch (Exception ex)
        {
          tcs.SetException(ex);
        }
      },
      null
    );

    return tcs.Task;
  }

  /// <summary>
  /// Used by the action block to invoke the actual method called by the UI.
  /// </summary>
  /// <param name="methodName"></param>
  /// <param name="args"></param>
  /// <exception cref="InvalidOperationException">The <see cref="BrowserBridge"/> was not initialized with an <see cref="IBinding"/> (see <see cref="AssociateWithBinding"/>)</exception>
  /// <exception cref="ArgumentException">The <paramref name="methodName"/> was not found or the given <paramref name="args"/> were not valid for the method call</exception>
  /// <exception cref="TargetInvocationException">The invoked method throws an exception</exception>
  /// <returns>The Json</returns>
  private async Task<object?> ExecuteMethod(string methodName, string args)
  {
    if (_binding is null)
    {
      throw new InvalidOperationException("Bridge was not initialized with a binding");
    }

    if (!_bindingMethodCache.TryGetValue(methodName, out MethodInfo method))
    {
      throw new ArgumentException(
        $"Cannot find method {methodName} in bindings class {_bindingType.NotNull().AssemblyQualifiedName}.",
        nameof(methodName)
      );
    }

    var parameters = method.GetParameters();
    var jsonArgsArray = JsonConvert.DeserializeObject<string[]>(args);
    if (parameters.Length != jsonArgsArray?.Length)
    {
      throw new ArgumentException(
        $"Wrong number of arguments when invoking binding function {methodName}, expected {parameters.Length}, but got {jsonArgsArray?.Length}.",
        nameof(args)
      );
    }

    var typedArgs = new object?[jsonArgsArray.Length];

    for (int i = 0; i < typedArgs.Length; i++)
    {
      var ccc = _jsonSerializer.Deserialize(jsonArgsArray[i], parameters[i].ParameterType);
      typedArgs[i] = ccc;
    }

    object? resultTyped;
    try
    {
      resultTyped = method.Invoke(Binding, typedArgs);
    }
    catch (TargetInvocationException ex)
    {
      throw new TargetInvocationException($"Unhandled exception while executing {methodName}", ex.InnerException);
    }

    // Was the method called async?
    if (resultTyped is not Task resultTypedTask)
    {
      // Regular method: no need to await things
      return resultTyped;
    }

    // It's an async call
    await resultTypedTask.ConfigureAwait(false);

    // If has a "Result" property return the value otherwise null (Task<void> etc)
    PropertyInfo? resultProperty = resultTypedTask.GetType().GetProperty(nameof(Task<object>.Result));
    object? taskResult = resultProperty?.GetValue(resultTypedTask);
    return taskResult;
  }

  /// <summary>
  /// Errors that not handled on bindings.
  /// </summary>
  private string SerializeFormattedException(Exception e)
  {
    //TODO: I'm not sure we still require this... the top level handler is already displaying the toast
    var errorDetails = new
    {
      Message = e.Message, // Topmost message
      Error = e.ToFormattedString(), // All messages from exceptions
      StackTrace = e.ToString(),
    };

    return _jsonSerializer.Serialize(errorDetails);
  }

  /// <summary>
  /// Notifies the UI that the method call is ready. We do not give the result back to the ui here via ExecuteScriptAsync
  /// because of limitations we discovered along the way (e.g, / chars need to be escaped).
  /// </summary>
  /// <param name="requestId"></param>
  /// <param name="serializedData"></param>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="IBrowserScriptExecutor.ExecuteScriptAsyncMethod"/></exception>
  private async Task NotifyUIMethodCallResultReady(
    string requestId,
    string? serializedData = null,
    CancellationToken cancellationToken = default
  )
  {
    _resultsStore[requestId] = serializedData;
    string script = $"{FrontendBoundName}.responseReady('{requestId}')";
    await _browserScriptExecutor.ExecuteScriptAsyncMethod(script, cancellationToken).ConfigureAwait(false);
  }

  /// <summary>
  /// Called by the ui to get back the serialized result of the method. See comments above for why.
  /// </summary>
  /// <param name="requestId"></param>
  /// <exception cref="ArgumentException">No result for the given <paramref name="requestId"/> was found</exception>
  /// <returns></returns>
  public string? GetCallResult(string requestId)
  {
    bool isFound = _resultsStore.TryRemove(requestId, out string? res);
    if (!isFound)
    {
      throw new ArgumentException($"No result for the given request id was found: {requestId}", nameof(requestId));
    }
    return res;
  }

  /// <summary>
  /// Shows the dev tools. This is currently only needed for CefSharp - other browser
  /// controls allow for right click + inspect.
  /// </summary>
  public void ShowDevTools()
  {
    _browserScriptExecutor.ShowDevTools();
  }

  [SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Url run as process")]
  public void OpenUrl(string url)
  {
    Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
  }

  public async Task Send(string eventName, CancellationToken cancellationToken = default)
  {
    if (_binding is null)
    {
      throw new InvalidOperationException("Bridge was not initialized with a binding");
    }

    var script = $"{FrontendBoundName}.emit('{eventName}')";

    await _browserScriptExecutor.ExecuteScriptAsyncMethod(script, cancellationToken).ConfigureAwait(false);
  }

  public async Task Send<T>(string eventName, T data, CancellationToken cancellationToken = default)
    where T : class
  {
    if (_binding is null)
    {
      throw new InvalidOperationException("Bridge was not initialized with a binding");
    }

    string payload = _jsonSerializer.Serialize(data);
    string requestId = $"{Guid.NewGuid()}_{eventName}";
    _resultsStore[requestId] = payload;
    var script = $"{FrontendBoundName}.emitResponseReady('{eventName}', '{requestId}')";
    await _browserScriptExecutor.ExecuteScriptAsyncMethod(script, cancellationToken).ConfigureAwait(false);
  }
}
