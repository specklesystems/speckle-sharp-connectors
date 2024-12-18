using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Registration;

public interface IConversionResult
{
  ConversionStatus ConversionStatus { get; }
  string? Message { get; }
  bool IsSuccess { get; }
}

public enum ConversionStatus
{
  Success,
  NoConverter,
  NoConversion
}

public readonly record struct ConverterResult<T>(ConversionStatus ConversionStatus, T? Converter = default, string? Message = null) : IConversionResult
{
  public bool IsSuccess => ConversionStatus == ConversionStatus.Success;
}

public readonly record struct BaseResult(ConversionStatus ConversionStatus, Base? Base = null, string? Message = null) : IConversionResult
{
  public bool IsSuccess => ConversionStatus == ConversionStatus.Success;
  
  public static BaseResult Success(Base baseObject) => new(ConversionStatus.Success, baseObject);
  public static BaseResult NoConverter(string? message) => new(ConversionStatus.NoConverter, Message: message);
}

public readonly record struct HostResult(ConversionStatus ConversionStatus, object? Host = null, string? Message = null) : IConversionResult
{
  public bool IsSuccess => ConversionStatus == ConversionStatus.Success;
  
  public static HostResult Success(object obj) => new(ConversionStatus.Success, obj);
  public static HostResult NoConverter(string? message) => new(ConversionStatus.NoConverter, Message: message);
  public static HostResult NoConversion(string? message) => new(ConversionStatus.NoConversion, Message: message);
}
