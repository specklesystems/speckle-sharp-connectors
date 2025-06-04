using Speckle.InterfaceGenerator;
using Speckle.Sdk;
using Speckle.Sdk.Common.Exceptions;
using Speckle.Sdk.Logging;

namespace Speckle.Connectors.Common.Operations;

[GenerateAutoInterface]
public class ReceiveConversionHandler(ISdkActivityFactory activityFactory) : IReceiveConversionHandler
{
  public Exception? TryConvert(Action conversion)
  {
    using var convertActivity = activityFactory.Start("Converting object");
    try
    {
      conversion();
      convertActivity?.SetStatus(SdkActivityStatusCode.Ok);
      return null;
    }
    catch (ConversionException ce)
    {
      //handle conversions but don't log to seq
      convertActivity?.SetStatus(SdkActivityStatusCode.Error);
      return ce;
    }
    catch (OperationCanceledException)
    {
      //handle conversions but don't log to seq and also throw
      convertActivity?.SetStatus(SdkActivityStatusCode.Error);
      throw;
    }
    catch (Exception ex) when (!ex.IsFatal())
    {
      convertActivity?.SetStatus(SdkActivityStatusCode.Error);
      convertActivity?.RecordException(ex);
      return ex;
    }
  }
}
