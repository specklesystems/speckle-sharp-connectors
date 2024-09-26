using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks.Dataflow;
using Microsoft.Extensions.Logging;
using Speckle.Connectors.DUI.Bindings;
using Speckle.Newtonsoft.Json;
using Speckle.Sdk.Common;
using Speckle.Sdk.Models.Extensions;

namespace Speckle.Connectors.DUI.Bridge;

/// <summary>
/// Wraps a binding class, and manages its calls from the Frontend to .NET, and sending events from .NET to the Frontend.
/// <para>Initially inspired by: https://github.com/johot/WebView2-better-bridge</para>
/// </summary>
[ClassInterface(ClassInterfaceType.AutoDual)]
[ComVisible(true)]
public sealed class BrowserBridge : IBrowserBridge
{
  private readonly JsonSerializerSettings _serializerOptions;
  private readonly ConcurrentDictionary<string, string?> _resultsStore = new();
  private readonly SynchronizationContext _mainThreadContext;
  private readonly ILogger _logger;
  private readonly IBrowserScriptExecutor _browserScriptExecutor;

  private IReadOnlyDictionary<string, MethodInfo>? _bindingMethodCache;
  private ActionBlock<RunMethodArgs>? _actionBlock;
  private IBinding? _binding;

  [MemberNotNull(nameof(_binding))]
  public IBinding Binding
  {
    get
    {
      AssertBindingInitialised();
      return _binding;
    }
    private set
    {
      if (_binding != null || this != value.Parent)
      {
        throw new ArgumentException($"Binding: {FrontendBoundName} is already bound or does not match bridge");
      }

      _binding = value;
    }
  }

  /// <inheritdoc/>
  public string FrontendBoundName => Binding.Name;
  private Type BindingType => Binding.GetType();

  public ITopLevelExceptionHandler TopLevelExceptionHandler { get; }

  private readonly record struct RunMethodArgs(string MethodName, string RequestId, string MethodArgs);

  /// <summary>
  /// Initializes a new instance of the <see cref="BrowserBridge"/> class.
  /// </summary>
  /// <param name="jsonSerializerSettings">The settings to use for JSON serialization and deserialization.</param>
  /// <param name="logger"></param>
  /// <param name="topLogger"></param>
  public BrowserBridge(
    JsonSerializerSettings jsonSerializerSettings,
    ILogger<BrowserBridge> logger,
    ILogger<TopLevelExceptionHandler> topLogger,
    IBrowserScriptExecutor browserScriptExecutor
  )
  {
    _serializerOptions = jsonSerializerSettings;
    _logger = logger;
    TopLevelExceptionHandler = new TopLevelExceptionHandler(topLogger, this);
    // Capture the main thread's SynchronizationContext
    _mainThreadContext = SynchronizationContext.Current.NotNull("No UI thread to capture?");
    _browserScriptExecutor = browserScriptExecutor;
  }

  /// <inheritdoc/>
  [MemberNotNull(nameof(_binding), nameof(_bindingMethodCache), nameof(_actionBlock))]
  public void AssociateWithBinding(IBinding binding)
  {
    // set via binding property to ensure explosion if already bound
    Binding = binding;

    // Note: we need to filter out getter and setter methods here because they are not really nicely
    // supported across browsers, hence the !method.IsSpecialName.
    var bindingMethodCache = new Dictionary<string, MethodInfo>();
    foreach (var m in BindingType.GetMethods().Where(method => !method.IsSpecialName))
    {
      bindingMethodCache[m.Name] = m;
    }
    _bindingMethodCache = bindingMethodCache;

    // Whenever the ui will call run method inside .net, it will post a message to this action block.
    // This conveniently executes the code outside the UI thread and does not block during long operations (such as sending).
    _actionBlock = new ActionBlock<RunMethodArgs>(
      OnActionBlock,
      new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1000 }
    );

    _logger.LogInformation("Bridge bound to front end name {FrontEndName}", binding.Name);
  }

  private async Task OnActionBlock(RunMethodArgs args)
  {
    Result<object?> result = await TopLevelExceptionHandler
      .CatchUnhandled(async () => await ExecuteMethod(args.MethodName, args.MethodArgs).ConfigureAwait(false))
      .ConfigureAwait(false);

    string resultJson = result.IsSuccess
      ? JsonConvert.SerializeObject(result.Value, _serializerOptions)
      : SerializeFormattedException(result.Exception);

    NotifyUIMethodCallResultReady(args.RequestId, resultJson);
  }

  /// <inheritdoc/>
  public string[] GetBindingsMethodNames()
  {
    AssertBindingInitialised();
    var bindingNames = _bindingMethodCache.Keys.ToArray();
    Debug.WriteLine($"{FrontendBoundName}: " + JsonConvert.SerializeObject(bindingNames, Formatting.Indented));
    return bindingNames;
  }

  /// <inheritdoc/>
  /// <remarks>
  /// This method posts the requested call to our action block executor.
  /// </remarks>
  public void RunMethod(string methodName, string requestId, string args)
  {
    AssertBindingInitialised();
    TopLevelExceptionHandler.CatchUnhandled(Post);
    return;

    void Post()
    {
      bool wasAccepted = _actionBlock
        .NotNull()
        .Post(
          new RunMethodArgs
          {
            MethodName = methodName,
            RequestId = requestId,
            MethodArgs = args
          }
        );
      if (!wasAccepted)
      {
        throw new InvalidOperationException($"Action block declined to Post ({methodName} {requestId} {args})");
      }
    }
  }

  /// <inheritdoc/>
  public void RunOnMainThread(Action action)
  {
    _mainThreadContext.Post(
      _ =>
      {
        // Execute the action on the main thread
        TopLevelExceptionHandler.CatchUnhandled(action);
      },
      null
    );
  }

  /// <summary>
  /// Used by the action block to invoke the .NET binding method called by the UI.
  /// </summary>
  /// <param name="methodName">The name of the .NET function to invoke</param>
  /// <param name="args">A JSON array of args to deserialize and use to invoke the method</param>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  /// <exception cref="ArgumentException">The <paramref name="methodName"/> was not found or the given <paramref name="args"/> were not valid for the method call</exception>
  /// <exception cref="TargetInvocationException">The invoked method throws an exception</exception>
  /// <returns>The JSON serialized result of the invoked method</returns>
  private async Task<object?> ExecuteMethod(string methodName, string args)
  {
    AssertBindingInitialised();

    if (!_bindingMethodCache.TryGetValue(methodName, out MethodInfo method))
    {
      throw new ArgumentException(
        $"Cannot find method {methodName} in bindings class {BindingType.AssemblyQualifiedName}.",
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
      var ccc = JsonConvert.DeserializeObject(jsonArgsArray[i], parameters[i].ParameterType, _serializerOptions);
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
  private string SerializeFormattedException(Exception ex)
  {
    //TODO: I'm not sure we still require this... the top level handler is already displaying the toast
    var errorDetails = new
    {
      Message = ex.Message, // Topmost message
      Error = ex.ToFormattedString(), // All messages from exceptions
      StackTrace = ex.ToString(),
    };

    return JsonConvert.SerializeObject(errorDetails, _serializerOptions);
  }

  /// <summary>
  /// Notifies the UI that the method call is ready. We do not give the result back to the ui here via ExecuteScriptAsync
  /// because of limitations we discovered along the way (e.g, / chars need to be escaped).
  /// </summary>
  /// <param name="requestId"></param>
  /// <param name="serializedData"></param>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="IBrowserScriptExecutor.ExecuteScriptAsyncMethod"/></exception>
  private void NotifyUIMethodCallResultReady(string requestId, string? serializedData = null)
  {
    _resultsStore[requestId] = serializedData;
    string script = $"{FrontendBoundName}.responseReady('{requestId}')";
    _browserScriptExecutor.ExecuteScriptAsyncMethod(script);
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

  /// <summary>
  /// Sends an event message to the JS browser (via the <see cref="IBrowserScriptExecutor"/>)
  /// </summary>
  /// <param name="eventName">the name of the JS event to trigger for the associated <see cref="FrontendBoundName"/></param>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="AssertBindingInitialised"/></exception>
  /// <exception cref="InvalidOperationException"><inheritdoc cref="IBrowserScriptExecutor.ExecuteScriptAsyncMethod"/></exception>
  public void Send(string eventName)
  {
    AssertBindingInitialised();

    var script = $"{FrontendBoundName}.emit('{eventName}')";

    _browserScriptExecutor.ExecuteScriptAsyncMethod(script);
  }

  /// <inheritdoc cref="Send"/>
  /// <summary>
  /// <inheritdoc cref="Send"/>.<br/>
  /// This overload also serializes and stores <paramref name="data"/> in an internal <see cref="_resultsStore"/> (values exposed through <see cref="GetCallResult"/>)<br/></summary>
  /// <param name="data">The data to JSON serialize and store</param>
  /// <typeparam name="T"></typeparam>
  public void Send<T>(string eventName, T data)
    where T : class
  {
    AssertBindingInitialised();

    string payload = JsonConvert.SerializeObject(data, _serializerOptions);
    string requestId = $"{Guid.NewGuid()}_{eventName}";
    _resultsStore[requestId] = payload;
    var script = $"{FrontendBoundName}.emitResponseReady('{eventName}', '{requestId}')";
    _browserScriptExecutor.ExecuteScriptAsyncMethod(script);
  }

  /// <inheritdoc/>
  [MemberNotNull(nameof(_binding), nameof(_bindingMethodCache), nameof(_actionBlock))]
  public void AssertBindingInitialised()
  {
    if (_binding is null || _bindingMethodCache is null || _actionBlock is null)
    {
      throw new InvalidOperationException("Bridge was not initialized with a binding");
    }
  }
}
