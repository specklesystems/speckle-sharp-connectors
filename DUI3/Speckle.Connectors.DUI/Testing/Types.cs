namespace Speckle.Connectors.DUI.Testing;

public record ModelTest(string Name, string Status);

public record ModelTestResult(string Name, string Status, string TimeStamp);

public record TestResults(string ModelName, string TestName, string Results, DateTime? TimeStamp = null);
