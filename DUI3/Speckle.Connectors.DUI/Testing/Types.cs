namespace Speckle.Connectors.DUI.Testing;


public record ModelTest(string Name);
public record ModelTestResult(string Model, string Test, string Results, string TimeStamp);
public record TestResults(string ModelName, string TestName, string Results, DateTime? TimeStamp = null);
