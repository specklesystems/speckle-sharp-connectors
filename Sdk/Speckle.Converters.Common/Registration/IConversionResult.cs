using Speckle.Sdk;
using Speckle.Sdk.Models;

namespace Speckle.Converters.Common.Registration;

public interface IConversionResult
{
  ConversionStatus ConversionStatus { get; }
  string? Message { get; }
  bool IsSuccess { get; }
  bool IsFailure { get; }
}

public enum ConversionStatus
{
  Success,
  NoConverter,
  NoConversion
}

public readonly record struct ConverterResult<T>(
  ConversionStatus ConversionStatus,
  T? Converter = default,
  string? Message = null
) : IConversionResult
{
  public bool IsSuccess => ConversionStatus == ConversionStatus.Success;
  public bool IsFailure => ConversionStatus != ConversionStatus.Success;
}

public readonly record struct BaseResult(ConversionStatus ConversionStatus, Base? Base = null, string? Message = null)
  : IConversionResult
{
  public bool IsSuccess => ConversionStatus == ConversionStatus.Success;
  public bool IsFailure => ConversionStatus != ConversionStatus.Success;

  public Base Value
  {
    get
    {
      if (IsSuccess)
      {
        return Base ?? throw new InvalidOperationException("Base was null");
      }
#pragma warning disable CA1065
      throw new SpeckleException("Conversion wasn't successful: " + Message);
#pragma warning restore CA1065
    }
  } 

  public static BaseResult Success(Base baseObject) => new(ConversionStatus.Success, baseObject);

  public static BaseResult NoConverter(string? message) => new(ConversionStatus.NoConverter, Message: message);

  public static BaseResult NoConversion(string? message) => new(ConversionStatus.NoConversion, Message: message);
}

public readonly record struct HostResult(ConversionStatus ConversionStatus, object? Host = null, string? Message = null)
  : IConversionResult
{
  public bool IsSuccess => ConversionStatus == ConversionStatus.Success;
  public bool IsFailure => ConversionStatus != ConversionStatus.Success;

  public static HostResult Success(object obj) => new(ConversionStatus.Success, obj);

  public static HostResult NoConverter(string? message) => new(ConversionStatus.NoConverter, Message: message);

  public static HostResult NoConversion(string? message) => new(ConversionStatus.NoConversion, Message: message);
}
