using Speckle.Sdk.Credentials;

namespace Speckle.Connectors.Common.Operations;

public record SendInfo(Account Account,string ProjectId, string ModelId);
