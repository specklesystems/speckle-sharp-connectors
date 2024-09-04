using Speckle.Autofac.DependencyInjection;
using Speckle.Connectors.Utils.Builders;
using Speckle.Converters.Common;

namespace Speckle.Connectors.Utils.Operations;

public abstract class ScopedSender(
  IUnitOfWorkFactory unitOfWorkFactory,
  IOperationProgressManager operationProgressManager
)
{
  protected async Task<SendOperationResult> SendOperation<TSendItem, TConversionSettings>(
    IBrowserBridge parent,
    SendInfo sendInfo,
    string modelCardId,
    IReadOnlyList<TSendItem> items,
    TConversionSettings conversionSettings,
    CancellationToken ct = default
  )
    where TConversionSettings : class
  {
    using var unitOfWork = unitOfWorkFactory.Create();
    using var settings = unitOfWork
      .Resolve<IConverterSettingsStore<TConversionSettings>>()
      .Push(_ => conversionSettings);

    return await unitOfWork
      .Resolve<SendOperation<TSendItem>>()
      .Execute(
        items,
        sendInfo,
        (status, progress) =>
          operationProgressManager.SetModelProgress(
            parent,
            modelCardId,
            new ModelCardProgress(modelCardId, status, progress),
            ct
          ),
        ct
      )
      .ConfigureAwait(false);
  }

  protected async Task<HostObjectBuilderResult> ReceiveOperation<TConversionSettings>(
    IBrowserBridge parent,
    ReceiveInfo receiveInfo,
    string modelCardId,
    TConversionSettings conversionSettings,
    CancellationToken ct = default
  )
    where TConversionSettings : class
  {
    using var unitOfWork = unitOfWorkFactory.Create();
    using var settings = unitOfWork
      .Resolve<IConverterSettingsStore<TConversionSettings>>()
      .Push(_ => conversionSettings);

    return await unitOfWork
      .Resolve<ReceiveOperation>()
      .Execute(
        receiveInfo,
        ct,
        (status, progress) =>
          operationProgressManager.SetModelProgress(
            parent,
            modelCardId,
            new ModelCardProgress(modelCardId, status, progress),
            ct
          )
      )
      .ConfigureAwait(false);
  }
}

public interface IOperationProgressManager
{
  void SetModelProgress(
    IBrowserBridge bridge,
    string modelCardId,
    ModelCardProgress progress,
    CancellationToken cancellationToken
  );
}

/// <summary>
/// Progress value between 0 and 1 to calculate UI progress bar width.
/// If it is null it will swooshing on UI.
/// </summary>
public record ModelCardProgress(string ModelCardId, string Status, double? Progress);

public interface IBrowserBridge
{
  /// <param name="eventName"></param>
  /// <exception cref="InvalidOperationException">Bridge was not initialized with a binding</exception>
  public void Send(string eventName);

  /// <inheritdoc cref="Send(string)"/>
  /// <param name="data">data to store</param>
  /// <typeparam name="T"></typeparam>
  /// <exception cref="InvalidOperationException">Bridge was not initialized with a binding</exception>
  public void Send<T>(string eventName, T data)
    where T : class;
}
