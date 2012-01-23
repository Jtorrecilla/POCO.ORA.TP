using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace POCO.Ora.TP
{
    internal class SequenceAttribute : System.Attribute 
    {
        public SequenceAttribute(string sequenceName)
        {
            if (string.IsNullOrWhiteSpace(sequenceName)) throw new Exception("Sequence name is necessary.");
            SequenceName = sequenceName;
        }

        public string SequenceName { get; set; }
    }
}
