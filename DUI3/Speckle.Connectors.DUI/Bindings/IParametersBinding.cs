namespace Speckle.Connectors.DUI.Bindings;

public interface IParametersBinding : IBinding
{
  public Task<ParameterUpdateSummary> Update(string payload);
}
