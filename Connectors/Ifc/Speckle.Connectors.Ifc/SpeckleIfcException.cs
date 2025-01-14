using Speckle.Sdk;

namespace Speckle.Connectors.Ifc;

public class SpeckleIfcException : SpeckleException
{
  public SpeckleIfcException() { }

  public SpeckleIfcException(string? message)
    : base(message) { }

  public SpeckleIfcException(string? message, Exception? inner = null)
    : base(message, inner) { }
}
