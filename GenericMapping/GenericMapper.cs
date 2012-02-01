using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;
using System.Reflection;
using POCO.Ora.TP.GenericMapping;
namespace POCO.Ora.TP.GenericMapping
{
    public class GenericMapper<T> : Mapper,IGenericMapper<T>
    {
        public GenericMapper()
        {
            Columns = new List<OracleColumnInfo>();
        }

        public IGenericMapper<T> AddColumnMapping(Expression<Func<T, object>> property)
        {
            PropertyInfo propertyInfo = GetProperty(property);
            return this;
        }
        
        public IGenericMapper<T> SetPrimaryKeyField(Expression<Func<T, object>> property)
        {
            PropertyInfo propertyInfo = GetProperty(property);
            AddColumn(propertyInfo);
            Columns.Where(col => col.ColumnName.Equals(propertyInfo.Name)).FirstOrDefault().IsPk = true;
            return this;
        }
        
        public IGenericMapper<T> SetNullable(Expression<Func<T, object>> property)
        {
            PropertyInfo propertyInfo = GetProperty(property);
            AddColumn(propertyInfo);
            Columns.Where(col => col.ColumnName.Equals(propertyInfo.Name)).FirstOrDefault().Nullable = true;
            return this;
        }
        public IGenericMapper<T> SetDefaultValue(Expression<Func<T, object>> property, string DefaultValue)
        {
            PropertyInfo propertyInfo = GetProperty(property);
            AddColumn(propertyInfo);
            Columns.Where(col => col.ColumnName.Equals(propertyInfo.Name)).FirstOrDefault().DefaultValue = DefaultValue;
            return this;
        }

        private void AddColumn(PropertyInfo propertyInfo)
        {
            var column = Columns.Where(col => col.ColumnName.Equals(propertyInfo.Name)).FirstOrDefault();
            if (column == null)
                Columns.Add(new OracleColumnInfo { ColumnName = propertyInfo.Name });
        }
        private static PropertyInfo GetProperty(Expression<Func<T, object>> property)
        {
            PropertyInfo propertyInfo = null;
            if (property.Body is MemberExpression)
            {
                propertyInfo = (property.Body as MemberExpression).Member as PropertyInfo;
            }
            else
            {
                propertyInfo = (((UnaryExpression)property.Body).Operand as MemberExpression).Member as PropertyInfo;
            }

            return propertyInfo;
        }
        
        public IGenericMapper<T> SetSequence(Expression<Func<T, object>> property, string SequenceName)
        {
            PropertyInfo propertyInfo = GetProperty(property);
            AddColumn(propertyInfo);
            Columns.Where(col => col.ColumnName.Equals(propertyInfo.Name)).FirstOrDefault().SequenceName = SequenceName;
            return this;
        }
                
    }
}
