namespace Speckle.Connectors.Common;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class FeatureImpactAttribute(params string[] features) : Attribute
{
  public string[] Features { get; } = features;
}
