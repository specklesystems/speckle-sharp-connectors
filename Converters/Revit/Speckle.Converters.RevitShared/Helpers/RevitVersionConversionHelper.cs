<<<<<<<< HEAD:Converters/Revit/Speckle.Converters.RevitShared/Helpers/RevitVersionConversionHelper.cs
﻿namespace Speckle.Converters.RevitShared.Helpers;
========
﻿using Speckle.Converters.RevitShared;

namespace Speckle.Converters.Revit2025;
>>>>>>>> origin/dev:Converters/Revit/Speckle.Converters.Revit2025/RevitVersionConversionHelper.cs

public class RevitVersionConversionHelper : IRevitVersionConversionHelper
{
  public bool IsCurveClosed(DB.NurbSpline nurbsSpline)
  {
    try
    {
      return nurbsSpline.IsClosed;
    }
    catch (Autodesk.Revit.Exceptions.ApplicationException)
    {
      // POC: is this actually a good assumption?
      return true;
    }
  }
}
