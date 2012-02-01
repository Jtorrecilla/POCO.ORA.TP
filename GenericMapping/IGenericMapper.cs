using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Linq.Expressions;

namespace POCO.Ora.TP.GenericMapping
{
    public interface IGenericMapper<T>
    {
        IGenericMapper<T> SetPrimaryKeyField(Expression<Func<T, object>> property);
        IGenericMapper<T> AddColumnMapping(Expression<Func<T, object>> property);
        IGenericMapper<T> SetNullable(Expression<Func<T, object>> property);
        IGenericMapper<T> SetDefaultValue(Expression<Func<T, object>> property, string DefaultValue);
        IGenericMapper<T> SetSequence(Expression<Func<T, object>> property, string SequenceName);
    }
}
