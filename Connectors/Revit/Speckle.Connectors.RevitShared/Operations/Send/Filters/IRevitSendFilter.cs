﻿using Speckle.Converters.RevitShared.Helpers;

namespace Speckle.Connectors.RevitShared.Operations.Send.Filters;

public interface IRevitSendFilter
{
  public void SetContext(IRevitContext revitContext);
}
