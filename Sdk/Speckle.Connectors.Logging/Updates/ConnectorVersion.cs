namespace Speckle.Connectors.Logging.Updates;

public readonly struct ConnectorVersion
{
  public string Number { get; init; }
  public Uri Url { get; init; }
  public int Os { get; init; } //These are enums, they are properly defined in the old v2 SDK (used by Speckle.Manager.Feed)
  public int Architecture { get; init; } //These are enums, they are properly defined in the old v2 SDK (used by Speckle.Manager.Feed)
  public DateTime Date { get; init; }
  public bool Prerelease { get; init; }
}
