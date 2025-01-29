using Speckle.Sdk.Models;

namespace Speckle.Converters.Common;

public interface ITypedConverter<in TIn, TOut>
{
  Result<TOut> Convert(TIn target);
}

public enum ConversionStatus
{
  Success,
  Warning,
  Error,
}

public interface IResult
{
  public bool IsSuccess { get; }
  public bool IsFailure { get; }

  ConversionStatus Status { get; }
  string? Message { get; }
}

public readonly struct Result<T>(ConversionStatus status, T? value, string? message = null) : IResult
{
  public bool IsSuccess => status == ConversionStatus.Success;
  public bool IsFailure => status != ConversionStatus.Success;
  public T Value => value ?? throw new InvalidOperationException("Result is not successful");
  public ConversionStatus Status => status;

  public string? Message => message;
}

public static class Result
{
  public static Result<Base> Base<T>(this Result<T> result)
    where T : Base => new(result.Status, result.IsSuccess ? (Base)result.Value : null, result.Message);

  public static Result<TChild> To<T, TChild>(this Result<T> result)
    where T : class, TChild
    where TChild : class => new(result.Status, result.IsSuccess ? result.Value : null, result.Message);

  public static Result<T> Success<T>(T value) => new(ConversionStatus.Success, value);

  public static Result<T> Warning<T>(string message) => new(ConversionStatus.Warning, default, message);

  public static Result<T> Error<T>(string message) => new(ConversionStatus.Error, default, message);

  public static Result<T> Failure<T>(this IResult other)
  {
    if (other.IsSuccess)
    {
      throw new InvalidOperationException("Cannot convert a successful result to a failure result");
    }
    return new Result<T>(other.Status, default, other.Message);
  }

  public static bool Try<T, TConverted>(
    this ITypedConverter<T, TConverted> converter,
    T val,
    out Result<TConverted> result
  )
  {
    result = converter.Convert(val);
    if (result.IsFailure)
    {
      return false;
    }

    return true;
  }
}
