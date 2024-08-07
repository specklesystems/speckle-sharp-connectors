using Speckle.Sdk;
using Speckle.Sdk.Host;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Utils;

public static class Config
{
  public static SpeckleConfiguration Create(HostApplication application, HostAppVersion version) =>
    new(
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
}
