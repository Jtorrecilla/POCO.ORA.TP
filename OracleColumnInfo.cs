using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace POCO.Ora.TP
{
    public class OracleColumnInfo
    {
        public OracleColumnInfo()
        {
            IsPk = false; 
            Nullable = false;
        }
        public bool IsPk { get; set; }
        public string ColumnName { get; set; }
        public bool Nullable { get; set; }
        public string DefaultValue { get; set; }
        public string SequenceName { get; set; }
    }

}
