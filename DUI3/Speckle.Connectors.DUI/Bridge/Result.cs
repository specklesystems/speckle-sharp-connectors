﻿using System.Diagnostics.CodeAnalysis;
using Speckle.Sdk.Common;

namespace Speckle.Connectors.DUI.Bridge;

/// <summary>
/// Result Pattern struct
/// </summary>
/// <typeparam name="T"></typeparam>
[ExcludeFromCodeCoverage]
public readonly struct Result<T>
{
  //Don't add new members to this struct, it is perfect.
  public T? Value { get; }
  public Exception? Exception { get; }

  [MemberNotNullWhen(false, nameof(Exception))]
  public bool IsSuccess => Exception is null;

  /// <summary>
  /// Create a successful result
  /// </summary>
  /// <param name="result"></param>
  public Result(T result)
  {
    Value = result;
  }

  /// <summary>
  /// Create a non-successful result
  /// </summary>
  /// <param name="result"></param>
  /// <exception cref="ArgumentNullException"><paramref name="result"/> was null</exception>
  public Result([NotNull] Exception? result)
  {
    Exception = result.NotNull();
  }
}

[ExcludeFromCodeCoverage]
public readonly struct Result
{
  //Don't add new members to this struct, it is perfect.
  public Exception? Exception { get; }

  [MemberNotNullWhen(false, nameof(Exception))]
  public bool IsSuccess => Exception is null;

  /// <summary>
  /// Create a non-successful result
  /// </summary>
  /// <param name="result"></param>
  /// <exception cref="ArgumentNullException"><paramref name="result"/> was null</exception>
  public Result([NotNull] Exception? result)
  {
    Exception = result.NotNull();
  }
}
