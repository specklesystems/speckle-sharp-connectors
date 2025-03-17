using Speckle.Sdk;

namespace Speckle.Importers.Ifc;

public class SpeckleIfcException : SpeckleException
{
  public SpeckleIfcException() { }

  public SpeckleIfcException(string? message)
    : base(message) { }

  public SpeckleIfcException(string? message, Exception? inner = null)
    : base(message, inner) { }
}
