using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Speckle.Connectors.Utils.Telemetry;

public class OpenTelemetryBuilder(IDisposable? traceProvider) : IDisposable
{
  private class MyProcessor : BaseProcessor<Activity>
  {
    public override void OnStart(Activity activity)
    {
      if (activity.IsAllDataRequested)
      {
        if (activity.Kind == ActivityKind.Server)
        {
          activity.SetTag("customServerTag", "Custom Tag Value for server");
        }
        else if (activity.Kind == ActivityKind.Client)
        {
          activity.SetTag("customClientTag", "Custom Tag Value for Client");
        }
      }
    }
  }

  public static IDisposable Initialize(string application)
  {
    var tracer = Sdk.CreateTracerProviderBuilder().AddSource(application);
    // .AddProcessor(new MyProcessor()) // This must be added before ConsoleExporter
    tracer.AddConsoleExporter();

    ActivityFactory.Initialize(application);

    return new OpenTelemetryBuilder(tracer.Build());
  }

  public void Dispose() => traceProvider?.Dispose();
}

public static class ActivityFactory
{
  private static ActivitySource? _activitySource;

  public static void Initialize(string application) => _activitySource = new ActivitySource(application, "1.0.0");

  public static Activity? Create(string name)
  {
    var activity = _activitySource.NotNull().StartActivity(name);
    return activity;
  }
}
