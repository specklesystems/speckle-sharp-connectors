using FluentAssertions;
using NUnit.Framework;
using Speckle.Connectors.Common.Cancellation;

namespace Speckle.Connectors.Common.Tests.Cancellation;

public class CancellationManagerTests
{
  [Test]
  public void CancelOne2()
  {
    var manager = new CancellationManager();
    manager.NumberOfOperations.Should().Be(0);

    var id = Guid.NewGuid().ToString();
    var item = manager.GetCancellationItem(id);

    manager.NumberOfOperations.Should().Be(1);
    manager.IsExist(id).Should().BeTrue();
    manager.CancelOperation(id);
    item.Token.IsCancellationRequested.Should().BeTrue();
    manager.IsCancellationRequested(id).Should().BeTrue();
    manager.IsExist(id).Should().BeTrue();
    item.Dispose();

    manager.IsExist(id).Should().BeFalse();
    item.Token.IsCancellationRequested.Should().BeTrue();

    manager.IsCancellationRequested(id).Should().BeTrue();
  }

  [Test]
  public void CancelTwo()
  {
    var manager = new CancellationManager();
    manager.NumberOfOperations.Should().Be(0);

    var id1 = Guid.NewGuid().ToString();
    var id2 = Guid.NewGuid().ToString();
    var item1 = manager.GetCancellationItem(id1);
    var item2 = manager.GetCancellationItem(id2);

    manager.NumberOfOperations.Should().Be(2);
    manager.IsExist(id1).Should().BeTrue();
    manager.CancelOperation(id1);
    item1.Token.IsCancellationRequested.Should().BeTrue();
    manager.IsCancellationRequested(id1).Should().BeTrue();
    manager.IsExist(id1).Should().BeTrue();
    item1.Dispose();

    manager.IsExist(id1).Should().BeFalse();
    item1.Token.IsCancellationRequested.Should().BeTrue();

    manager.IsCancellationRequested(id1).Should().BeTrue();

    manager.NumberOfOperations.Should().Be(1);
    manager.IsExist(id2).Should().BeTrue();
    manager.CancelOperation(id2);
    item2.Token.IsCancellationRequested.Should().BeTrue();
    manager.IsCancellationRequested(id2).Should().BeTrue();
    manager.IsExist(id2).Should().BeTrue();
    item2.Dispose();

    manager.IsExist(id2).Should().BeFalse();
    item2.Token.IsCancellationRequested.Should().BeTrue();

    manager.IsCancellationRequested(id2).Should().BeTrue();
  }

  [Test]
  public void CancelAll()
  {
    var manager = new CancellationManager();
    manager.NumberOfOperations.Should().Be(0);

    var id1 = Guid.NewGuid().ToString();
    var id2 = Guid.NewGuid().ToString();
    var item1 = manager.GetCancellationItem(id1);
    var item2 = manager.GetCancellationItem(id2);

    manager.NumberOfOperations.Should().Be(2);
    manager.IsExist(id1).Should().BeTrue();
    manager.CancelAllOperations();
    item1.Token.IsCancellationRequested.Should().BeTrue();
    item2.Token.IsCancellationRequested.Should().BeTrue();
    manager.IsCancellationRequested(id1).Should().BeTrue();
    manager.IsCancellationRequested(id2).Should().BeTrue();
    manager.IsExist(id1).Should().BeTrue();
    manager.IsExist(id2).Should().BeTrue();
    item1.Dispose();
    item2.Dispose();

    manager.IsExist(id1).Should().BeFalse();
    manager.IsExist(id2).Should().BeFalse();

    item1.Token.IsCancellationRequested.Should().BeTrue();
    item2.Token.IsCancellationRequested.Should().BeTrue();
  }

  [Test]
  public void Cancel_No_Existing()
  {
    var manager = new CancellationManager();
    manager.NumberOfOperations.Should().Be(0);
    var x = Guid.NewGuid().ToString();
    manager.IsCancellationRequested(x).Should().BeTrue();
    manager.IsExist(x).Should().BeFalse();
  }
}
