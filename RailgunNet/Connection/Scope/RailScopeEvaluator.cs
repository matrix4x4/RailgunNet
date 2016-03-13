﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Railgun
{
  public class RailScopeEvaluator
  {
    protected internal const int NEVER = -1;

    protected internal virtual bool IsInScope(RailEntity entity) 
    { 
      return true; 
    }

    protected internal virtual float GetPriority(RailEntity entity, int ticksSinceSend)
    { 
      return 1.0f; 
    }
  }
}
