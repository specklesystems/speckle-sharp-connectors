using Autodesk.Revit.DB;

namespace Speckle.Connectors.Revit.Operations.Receive;

public interface ITransactionManager : IDisposable
{
  TransactionStatus CommitSubtransaction();
  TransactionStatus CommitTransaction();
  void RollbackSubTransaction();
  void RollbackTransaction();
  void StartSubtransaction();

  // POC improve how the error handling behaviour is selected
  void StartTransaction(bool enableFailurePreprocessor = false, string name = "Speckle Transaction");
}
