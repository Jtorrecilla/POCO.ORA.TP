﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace POCO.Ora.TP
{
    internal class IsPKAttribute : System.Attribute  
    {
        public IsPKAttribute(bool isPK)
        {
            IsPK = isPK;
        }

        public bool IsPK { get; set; }
    }
}
