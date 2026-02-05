using System.Diagnostics.CodeAnalysis;

namespace Speckle.Connectors.Common.Tests.Threading;

[SuppressMessage("Design", "CA1008:Enums should have zero value")]
public enum Funcs
{
  RunMain,
  RunWorker,
  RunMainAsync,
  RunWorkerAsync,
  WorkerToMainAsync,
  MainToWorker,
  WorkerToMain,
  MainToWorkerAsync,
  RunMainAsync_T,
  RunWorkerAsync_T,
}
