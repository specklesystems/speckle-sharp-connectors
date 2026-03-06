namespace Speckle.Connectors.DUI.Bindings;

public interface IParametersBinding : IBinding
{
  public Task Update(string payload);
}
