using Autodesk.Revit.DB;
using Speckle.Converters.RevitShared.Helpers;
using Speckle.InterfaceGenerator;

namespace Speckle.Connectors.Revit.Operations.Receive;

/// <summary>
/// Is responsible for all functionality regarding subtransactions, transactions, and transaction groups.
/// This includes starting, pausing, committing, and rolling back transactions
/// </summary>
[GenerateAutoInterface]
public sealed class TransactionManager : ITransactionManager
{
  private sealed class GroupDisposible : IDisposable
  {
    private readonly TransactionGroup _transactionGroup;

    public GroupDisposible(TransactionGroup transactionGroup)
    {
      _transactionGroup = transactionGroup;
      _transactionGroup.Start();
    }

    public void Dispose()
    {
      _transactionGroup.Assimilate();
      _transactionGroup.Dispose();
    }
  }

  private sealed class TransactionDisposible : IDisposable
  {
    private readonly Transaction _transaction;

    public TransactionDisposible(Transaction transaction)
    {
      _transaction = transaction;
    }

    public void Dispose()
    {
      _transaction.Commit();
      _transaction.Dispose();
    }
  }

  private sealed class SubTransactionDisposible : IDisposable
  {
    private readonly SubTransaction _transaction;

    public SubTransactionDisposible(SubTransaction transaction)
    {
      _transaction = transaction;
    }

    public void Dispose()
    {
      _transaction.Commit();
      _transaction.Dispose();
    }
  }

  private readonly IRevitConversionContextStack _contextStack;
  private Document Document => _contextStack.Current.Document;

  public TransactionManager(IRevitConversionContextStack contextStack)
  {
    _contextStack = contextStack;
  }

  public IDisposable StartTransactionGroup(string projectName)
  {
    return new GroupDisposible(new TransactionGroup(_contextStack.Current.Document, $"Received data from {projectName}"));
  }

  public IDisposable StartTransaction()
  {
      var transaction = new Transaction(Document, "Speckle Transaction");
      var failOpts = transaction.GetFailureHandlingOptions();
      // POC: make sure to implement and add the failure preprocessor
      // https://spockle.atlassian.net/browse/DUI3-461
      //failOpts.SetFailuresPreprocessor(_errorPreprocessingService);
      failOpts.SetClearAfterRollback(true);
      transaction.SetFailureHandlingOptions(failOpts);
      transaction.Start();
      return new TransactionDisposible(transaction);
  }

  public IDisposable StartSubtransaction()
  {
      var subTransaction = new SubTransaction(Document);
      subTransaction.Start();
      return new SubTransactionDisposible(subTransaction);
  }
}
