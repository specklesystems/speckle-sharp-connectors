using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class ReceiveConversionHandler : IReceiveConversionHandler
{
  public Exception? TryConvert(Action conversion)
  {
    try
    {
      conversion();
      return null;
    }
    catch (ConversionException ce)
    {
      //handle conversions but don't log to seq
      return ce;
    }
    catch (OperationCanceledException)
    {
      //handle conversions but don't log to seq and also throw
      throw;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      return ex;
    }
  }
}
