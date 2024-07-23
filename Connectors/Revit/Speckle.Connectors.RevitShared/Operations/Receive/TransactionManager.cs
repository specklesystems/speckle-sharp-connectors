using System.Diagnostics.CodeAnalysis;
using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Revit.Operations.Receive;

public interface IRevitTransaction : IDisposable
{
  TransactionStatus Commit();
}

/// <summary>
/// Is responsible for all functionality regarding subtransactions, transactions, and transaction groups.
/// This includes starting, pausing, committing, and rolling back transactions
/// </summary>
[GenerateAutoInterface]
[SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope")]
public sealed class TransactionManager : ITransactionManager
{
  private sealed class GroupDisposable(TransactionGroup transactionGroup) : IRevitTransaction
  {
    public TransactionStatus Commit() => transactionGroup.Assimilate();

    public void Dispose() => transactionGroup.Dispose();
  }

  private sealed class TransactionDisposable(Transaction transaction) : IRevitTransaction
  {
    public TransactionStatus Commit() => transaction.Commit();

    public void Dispose() => transaction.Dispose();
  }

  private sealed class SubTransactionDisposable(SubTransaction transaction) : IRevitTransaction
  {
    public TransactionStatus Commit() => transaction.Commit();

    public void Dispose() => transaction.Dispose();
  }

  private readonly IRevitConversionContextStack _contextStack;
  private Document Document => _contextStack.Current.Document;

  public TransactionManager(IRevitConversionContextStack contextStack)
  {
    _contextStack = contextStack;
  }

  public IRevitTransaction StartTransactionGroup(string projectName)
  {
    var group = new TransactionGroup(_contextStack.Current.Document, $"Received data from {projectName}");
    group.Start();
    return new GroupDisposable(group);
  }

  public IRevitTransaction StartTransaction()
  {
    var transaction = new Transaction(Document, "Speckle Transaction");
    var failOpts = transaction.GetFailureHandlingOptions();
    // POC: make sure to implement and add the failure preprocessor
    // https://spockle.atlassian.net/browse/DUI3-461
    //failOpts.SetFailuresPreprocessor(_errorPreprocessingService);
    failOpts.SetClearAfterRollback(true);
    transaction.SetFailureHandlingOptions(failOpts);
    transaction.Start();
    return new TransactionDisposable(transaction);
  }

  public IRevitTransaction StartSubtransaction()
  {
    var subTransaction = new SubTransaction(Document);
    subTransaction.Start();
    return new SubTransactionDisposable(subTransaction);
  }
}
