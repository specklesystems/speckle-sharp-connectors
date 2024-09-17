using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Host;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Utils;

public static class Connector
{
  public static readonly string TabName = "Speckle";
  public static readonly string TabTitle = "Speckle (Beta)";

  public static HostAppVersion Version { get; private set; } = HostAppVersion.v3;
  public static string VersionString { get; private set; } = string.Empty;
  public static string Name => HostApp.Name;
  public static string Slug => HostApp.Slug;

  public static HostApplication HostApp { get; private set; }

  public static IDisposable? Initialize(HostApplication application, HostAppVersion version)
  {
    Version = version;
    VersionString = HostApplications.GetVersion(version);
    HostApp = application;
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);

#if DEBUG //temp change don't stage
    var config = new SpeckleConfiguration(
      application,
      version,
      new(MinimumLevel: SpeckleLogLevel.Information, Console: true, File: new(Path: "SpeckleCoreLog.txt")),
      new(Console: false, Otel: new())
    );
#else
    var config = new SpeckleConfiguration(
      application,
      version,
      new(
        MinimumLevel: SpeckleLogLevel.Information,
        Console: false,
        File: new(Path: "SpeckleCoreLog.txt"),
        Otel: new(
          Endpoint: "https://seq-dev.speckle.systems/ingest/otlp/v1/logs",
          Headers: new() { { "X-Seq-ApiKey", "y5YnBp12ZE1Czh4tzZWn" } }
        )
      ),
      new(
        Console: false,
        Otel: new(
          Endpoint: "https://seq-dev.speckle.systems/ingest/otlp/v1/traces",
          Headers: new() { { "X-Seq-ApiKey", "y5YnBp12ZE1Czh4tzZWn" } }
        )
      )
    );
#endif
    return Setup.Initialize(config);
  }
}
