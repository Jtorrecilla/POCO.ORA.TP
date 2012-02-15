using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Data;
using POCO.Ora.TP.GenericMapping;

namespace POCO.Ora.TP.Extensions
{
    public static class TableExtensionForList
    {
        public static DataTable GetTable<T>(this IList<T> obj, Mapper mapper)
        {
            DataTable dt = new DataTable();
            foreach (var col in mapper.Columns)
            {
                dt.Columns.Add(new DataColumn(col.ColumnName, typeof(T).GetProperty(col.ColumnName).PropertyType));
            }

            obj.ToList().ForEach(a =>
            {
                if (a != null)
                {
                    var row = dt.NewRow();

                    foreach (var property in typeof(T).GetProperties())
                    {
                        row[property.Name] = ((T)a).GetType().GetProperty(property.Name).GetValue(a, null);
                    }
                    dt.Rows.Add(row);
                }
            });

            return dt;
        }
    }
}
