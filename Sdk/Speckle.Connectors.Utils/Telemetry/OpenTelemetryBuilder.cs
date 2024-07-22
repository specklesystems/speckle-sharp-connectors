using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Exporter;
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
    TracerProviderBuilder tracer = Sdk.CreateTracerProviderBuilder().AddSource(application);
    // .AddProcessor(new MyProcessor()) // This must be added before ConsoleExporter
    // tracer.AddConsoleExporter();
    DoExtras(tracer);
    ActivityFactory.Initialize(application);

    return new OpenTelemetryBuilder(tracer.Build());
  }

  public static void Register(object obj)
  {
    var diAssembly = AppDomain
      .CurrentDomain.GetAssemblies()
      .First(x => x.FullName.Contains("Microsoft.Extensions.DependencyInjection.Abstractions"));
    var serviceCollectionType = diAssembly.DefinedTypes.First(x => x.Name == "IServiceCollection");
    var serviceCollectionExtensionsType = diAssembly.ExportedTypes.First(x =>
      x.Name == "ServiceCollectionServiceExtensions"
    );
    var methods = serviceCollectionExtensionsType
      .GetMethods(BindingFlags.Static | BindingFlags.Public)
      .First(x =>
        x.Name == "AddSingleton"
        && x.GetParameters()
          .Select(y => y.ParameterType)
          .SequenceEqual([serviceCollectionType, typeof(Type), typeof(Type)])
      );
    methods.Invoke(
      null,
      new object[] { obj, typeof(IOptionsFactory<OtlpExporterOptions>), typeof(CustomOptionsFactory) }
    );
    Console.WriteLine("here ");
  }

  public static void DoExtras(TracerProviderBuilder tracer)
  {
    var diAssembly = AppDomain
      .CurrentDomain.GetAssemblies()
      .First(x => x.FullName.Contains("Microsoft.Extensions.DependencyInjection.Abstractions"));
    var serviceCollectionType = diAssembly.DefinedTypes.First(x => x.Name == "IServiceCollection");

    var actionType = typeof(Action<>).MakeGenericType(serviceCollectionType);
    var @delegate = Delegate.CreateDelegate(actionType, null, typeof(OpenTelemetryBuilder).GetMethod("Register"));

    typeof(OpenTelemetryDependencyInjectionTracerProviderBuilderExtensions)
      .GetMethods()
      .First(x => x.Name == "ConfigureServices")
      .Invoke(null, [tracer, @delegate]);
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

public class CustomOptionsFactory : IOptionsFactory<OtlpExporterOptions>
{
  public OtlpExporterOptions Create(string name)
  {
    return new OtlpExporterOptions();
  }
}
