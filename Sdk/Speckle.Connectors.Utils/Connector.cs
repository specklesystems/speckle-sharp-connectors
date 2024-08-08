using Speckle.Objects.Geometry;
using Speckle.Sdk;
using Speckle.Sdk.Host;
using Speckle.Sdk.Logging;
using Speckle.Sdk.Models;

namespace Speckle.Connectors.Utils;

public static class Connector
{
  public static IDisposable? Initialize(HostApplication application, HostAppVersion version)
  {
    TypeLoader.Initialize(typeof(Base).Assembly, typeof(Point).Assembly);
    var config = new SpeckleConfiguration(
      application,
      version,
      new(
        MinimumLevel: SpeckleLogLevel.Information,
        Console: true,
        File: new(Path: "SpeckleCoreLog.txt"),
        Otel: new(
          Endpoint: "https://seq.speckle.systems/ingest/otlp/v1/logs",
          Headers: new() { { "X-Seq-ApiKey", "agZqxG4jQELxQQXh0iZQ" } }
        )
      ),
      new(
        Console: false,
        Otel: new(
          Endpoint: "https://seq.speckle.systems/ingest/otlp/v1/traces",
          Headers: new() { { "X-Seq-ApiKey", "agZqxG4jQELxQQXh0iZQ" } }
        )
      )
    );
    return Setup.Initialize(config);
  }
}
