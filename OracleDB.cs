using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using Oracle.DataAccess.Client;
using System.ComponentModel;
using System.Windows.Forms;
using System.Reflection;
using System.Linq.Expressions;
using System.Data;
using POCO.Ora.TP.GenericMapping;

namespace POCO.Ora.TP
{


    //''' Features 1.0.3
    // 1) IsPK(bool) => IsPK()
    // 2) SequenceAttribute
    // 3) Recfactor
    // 4) Avoid null values
    //''' Features 1.0.4
    // 1) Base Type por Entities
    // 2) UoW

    public class OracleDB : IDisposable
    {
        private OracleConnection connection;
        private string _connectionString;
        private string _schemmaName;
        private List<object> insertCommands;
        private List<object> updateCommands;
        private List<object> deleteCommands;
        public OracleDB(string schemmaName, Enums.Mode connectionMode = Enums.Mode.Normal)
        {
            Init(schemmaName, connectionMode);
            _connectionString = System.Configuration.ConfigurationManager.ConnectionStrings[System.Configuration.ConfigurationManager.ConnectionStrings.Count - 1].ConnectionString;
        }

        private void Init(string schemmaName, Enums.Mode connectionMode)
        {
            _schemmaName = schemmaName;
            ConnectionMode = connectionMode;
            insertCommands = new List<object>();
            updateCommands = new List<object>();
            deleteCommands = new List<object>();
        }

        public OracleDB(string connectionStringName, string schemmaName, Enums.Mode connectionMode = Enums.Mode.Normal)
        {

            Init(schemmaName, connectionMode);
            _connectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
        }

        public void Insert<T>(T entity)
        {
            var mapper = GetMapper(typeof(T));

            using (var command = (mapper.Value != null ? GetMappedInsertCommand(entity, mapper) : GetInsertCommand(entity)))
            {
                ExecuteMethod(command);
            }

        }


        public void Update<T>(T entity)
        {
            using (var command = GetUpdateCommand(entity))
            {
                ExecuteMethod(command);
            }
        }
        public void Delete<T>(T entity) 
        {
            using (var command = GetDeleteCommand(entity))
            {
                ExecuteMethod(command);
            }
        }
        public void Save()
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork)
            {
                CheckConnection();
                var transaction = connection.BeginTransaction();
                try
                {
                    ExecuteInserts();
                    ExecuteUpdates();
                    ExecuteDeletes();
                    ClearCommands();
                    transaction.Commit();
                }
                catch (Exception ex)
                {
                    transaction.Rollback();
                    throw ex;
                }
            }

        }

        private void ClearCommands()
        {
            insertCommands.Clear();
            updateCommands.Clear();
            deleteCommands.Clear();
        }

        private void ExecuteDeletes()
        {
            foreach (var element in deleteCommands)
            {
                //OracleCommand command = GetMappedInsertCommand(element);
                //command.Connection = connection;
                //command.ExecuteNonQuery();
            }
        }

        private void ExecuteUpdates()
        {
            foreach (var element in updateCommands)
            {
                var mapper = GetMapper(element.GetType());

                using (var command = (mapper.Value != null ? GetMappedUpdateCommand(element, mapper) : GetUpdateCommand(element)))
                {
                    command.Connection = connection;
                    command.ExecuteNonQuery();
                }
            }
        }

        private void ExecuteInserts()
        {
            foreach (var element in insertCommands)
            {
                var mapper = GetMapper(element.GetType());

                using (var command = (mapper.Value != null ? GetMappedInsertCommand(element, mapper) : GetInsertCommand(element)))
                {
                    command.Connection = connection;
                    command.ExecuteNonQuery();
                }
            }
        }

        #region PrivateMethods
        private void ExecuteMethod(OracleCommand command)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork) return;
            CheckConnection();
            command.Connection = connection;
            var transaction = connection.BeginTransaction();
            try
            {
                command.ExecuteNonQuery();
                transaction.Commit();
            }
            catch (OracleException ex)
            {

                transaction.Rollback();
                throw ex;
            }
        }
        private void CheckConnection()
        {
            if (connection == null) connection = new Oracle.DataAccess.Client.OracleConnection(_connectionString);
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();
        }
        private OracleCommand GetInsertCommand(object entity)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                insertCommands.Add(entity);
                return null;
            }

            OracleCommand cmd = new OracleCommand();
            string commandText = string.Empty;
            CommaDelimitedStringCollection columns = new CommaDelimitedStringCollection();
            CommaDelimitedStringCollection parameters = new CommaDelimitedStringCollection();

            foreach (var prop in entity.GetType().GetProperties())
            {
                var value = prop.GetValue(entity, null);
                var seq = prop.GetCustomAttributes(false).Where(a => a is SequenceAttribute).FirstOrDefault();
                var defaultValue = prop.GetCustomAttributes(false).Where(a => a is DefaultValueAttribute).FirstOrDefault();
                columns.Add(String.Format("{0}", prop.Name));
                if (seq != null)
                {
                    parameters.Add(String.Format("{0}.{1}.NextVal", _schemmaName, (seq as SequenceAttribute).SequenceName));
                }
                else
                {
                    if (defaultValue == null)
                    {
                        parameters.Add(String.Format(":{0}", prop.Name));
                        if (prop.PropertyType.Name.StartsWith("Nullable") && value == null)
                        {
                            cmd.Parameters.Add(new OracleParameter(prop.Name, DBNull.Value));
                        }
                        else
                        {
                            cmd.Parameters.Add(new OracleParameter(prop.Name, value));
                        }
                    }
                    else
                    {
                        parameters.Add(String.Format("{0}", (defaultValue as DefaultValueAttribute).Value.ToString()));
                    }
                }

            }
    
            commandText=String.Format("INSERT INTO {0}.{1} ({2}) VALUES ({3})", 
                                        _schemmaName, 
                                        entity.GetType().Name, 
                                        columns.ToString(), 
                                        parameters.ToString());
            cmd.CommandText = commandText.ToString();
            return cmd;

        }
        private OracleCommand GetMappedInsertCommand(object entity, KeyValuePair<Mapper, Type> map)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                insertCommands.Add(entity);
                return null;
            }

            OracleCommand cmd = new OracleCommand();
            string commandText = string.Empty;
            CommaDelimitedStringCollection col = new CommaDelimitedStringCollection();
            CommaDelimitedStringCollection param = new CommaDelimitedStringCollection();
            col.AddRange(map.Key.Columns.Select(a => a.ColumnName).ToArray());
            foreach (var column in map.Key.Columns)
            {
                var prop = entity.GetType().GetProperty(column.ColumnName);
                var value = prop.GetValue(entity, null);
                if (String.IsNullOrWhiteSpace(column.DefaultValue))
                {
                    if (string.IsNullOrWhiteSpace(column.SequenceName))
                    {
                        param.Add(String.Format(":{0}", column.ColumnName));
                        if (column.Nullable && value == null)
                        {
                            cmd.Parameters.Add(new OracleParameter(column.ColumnName, DBNull.Value));
                        }
                        else
                        {
                            cmd.Parameters.Add(new OracleParameter(column.ColumnName, value));
                        }
                    }
                    else
                    {
                        param.Add(String.Format("{0}.{1}.NextVal", _schemmaName, column.SequenceName));
                    }

                }
                else
                {
                    param.Add(column.DefaultValue);
                }

            }
            commandText = String.Format("INSERT INTO {0}.{1} ({2}) VALUES ({3})",
                                        _schemmaName,
                                        entity.GetType().Name,
                                        col.ToString(),
                                        param.ToString());
            cmd.CommandText = commandText.ToString();
            return cmd;

        }

        private OracleCommand GetMappedUpdateCommand(object entity, KeyValuePair<Mapper, Type> map)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                updateCommands.Add(entity);
                return null;
            }
            OracleCommand cmd = new OracleCommand();
            string commandText = string.Empty;
            CommaDelimitedStringCollection columns = new CommaDelimitedStringCollection();
            StringBuilder where = new StringBuilder();
            foreach (var column in map.Key.Columns)
            {
                var prop = entity.GetType().GetProperty(column.ColumnName);
                var value = prop.GetValue(entity, null);
                if (column.IsPk)
                {
                    where.Append(String.Format("{0} = :{0} AND", column.ColumnName));
                    cmd.Parameters.Add(new OracleParameter(column.ColumnName, value));
                }
                else
                {
                    columns.Add(String.Format("{0} = :{0},", column.ColumnName));
                    if (column.Nullable && value == null)
                    {
                        cmd.Parameters.Add(new OracleParameter(column.ColumnName, DBNull.Value));
                    }
                    else
                    {
                        cmd.Parameters.Add(new OracleParameter(column.ColumnName, value));
                    }
                }

            }
            where.Remove(where.Length - 3, 3);
            commandText = String.Format("UPDATE {0}.{1} SET {2} WHERE {3}",
                                    _schemmaName,
                                    entity.GetType().Name,
                                    columns.ToString(),
                                    where.ToString());
            cmd.CommandText = commandText;
            return cmd;

        }

        private KeyValuePair<Mapper, Type> GetMapper(Type type)
        {
            var map = Mapping.Where(
               a => a.Value.Equals(type)
                            ).FirstOrDefault();
            return map;
        }

        private OracleCommand GetUpdateCommand(object entity)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                updateCommands.Add(entity);
                return null;
            }
            OracleCommand cmd = new OracleCommand();
            StringBuilder builder = new StringBuilder();
            StringBuilder columns = new StringBuilder();
            StringBuilder where = new StringBuilder();

            foreach (var prop in entity.GetType().GetProperties())
            {
                if (prop.GetCustomAttributes(false).Any(a => a is IsPKAttribute))
                {
                    where.Append(String.Format("{0} = :{0} AND", prop.Name));
                }
                else
                {
                    columns.Append(String.Format("{0} = :{0},", prop.Name));
                    if (prop.PropertyType.Name.StartsWith("Nullable") && prop.GetValue(entity, null) == null)
                    {
                        cmd.Parameters.Add(new OracleParameter(prop.Name, DBNull.Value));
                    }
                    else
                    {
                        cmd.Parameters.Add(new OracleParameter(prop.Name, prop.GetValue(entity, null)));
                    }

                }
            }
            columns.Remove(columns.Length - 1, 1);
            where.Remove(where.Length - 3, 3);

            builder.Append(String.Format("UPDATE {0}.{1} SET {2} WHERE {3}", _schemmaName, entity.GetType().Name, columns, where));
            cmd.CommandText = builder.ToString();
            return cmd;

        }

        private OracleCommand GetDeleteCommand(object entity)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                deleteCommands.Add(entity);
                return null;
            }
            OracleCommand cmd = new OracleCommand();
            StringBuilder builder = new StringBuilder();
            StringBuilder where = new StringBuilder();

            foreach (var prop in entity.GetType().GetProperties())
            {
                if (prop.GetCustomAttributes(false).Any(a => a is IsPKAttribute))
                {

                    where.Append(String.Format("{0} = :{0} AND", prop.Name));
                    cmd.Parameters.Add(new OracleParameter(prop.Name, prop.GetValue(entity, null)));
                }
            }
            where.Remove(where.Length - 3, 3);

            builder.Append(String.Format("DELETE FROM {0}.{1} WHERE {2}", _schemmaName, entity.GetType().Name, where));
            cmd.CommandText = builder.ToString();
            return cmd;

        }


        private OracleCommand GetMapppedDeleteCommand(object entity, KeyValuePair<Mapper, Type> map)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                deleteCommands.Add(entity);
                return null;
            }
            OracleCommand cmd = new OracleCommand();
            string commandText = string.Empty;
            StringBuilder where = new StringBuilder();
            foreach (var column in map.Key.Columns.Where(col=>col.IsPk))
            {
                var prop = entity.GetType().GetProperty(column.ColumnName);
                var value = prop.GetValue(entity, null);
                where.Append(String.Format("{0} = :{0} AND", column.ColumnName));
                cmd.Parameters.Add(new OracleParameter(column.ColumnName, value));

            }
            
            where.Remove(where.Length - 3, 3);

            commandText=String.Format("DELETE FROM {0}.{1} WHERE {2}", _schemmaName, entity.GetType().Name, where);
            cmd.CommandText = commandText.ToString();
            return cmd;

        }
        private OracleCommand GetQueryCommand<T>(string columns, long take, long skip, string order, string where, object[] args)
        {
            var command = new OracleCommand(GetQueryText<T>(columns, take, skip, order, where, args), connection);
            if (args != null)
            {
                for (int i = 0; i < args.Count(); i++)
                {
                    command.Parameters.Add(new OracleParameter(i.ToString(), args[i]));
                }
            }
            return command;
        }
        private string GetQueryText<T>(string columns, long take, long skip, string order, string where, object[] args)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(String.Format("SELECT {1} FROM (select row_number() over (order by 1 DESC) r,{2}.{1} from  {0}.{2}", _schemmaName, columns, typeof(T).Name));
            if (!string.IsNullOrWhiteSpace(where))
            {
                builder.Append(String.Format(" WHERE {0} ", where));
            }
            builder.Append(" ) ");
            if (skip > 0 && take == 0)
            {
                builder.Append(String.Format(" WHERE r >{0}", skip));
            }
            if (take > 0 && skip == 0)
            {
                builder.Append(String.Format(" WHERE r BETWEEN 1 AND {0}", take));
            }
            if (take > 0 && skip > 0)
            {
                builder.Append(String.Format(" WHERE r between {0} and {1}", skip, skip + take));
            }
            //if (!string.IsNullOrWhiteSpace(where))
            //{
            //    if (builder.ToString().Contains("WHERE"))
            //        builder.Append(" AND ");
            //    else
            //        builder.Append(" WHERE ");
            //    builder.Append(where);
            //}
            if (!string.IsNullOrWhiteSpace(order))
            {
                builder.Append(String.Format(" ORDER BY {0}", order));
            }
            return builder.ToString();
        }
        private static void FetchReader<T>(List<T> lista, OracleDataReader reader) 
        {
            var converter = new TypeConverter();
            while (reader.Read())
            {
                var dato = typeof(T).GetConstructor(new Type[0]).Invoke(new object[0]);
                foreach (var prop in dato.GetType().GetProperties())
                {
                    if (reader.GetSchemaTable().Rows.Cast<DataRow>().Where(a => a.Field<string>("COLUMNNAME").Equals(prop.Name)).Any())
                    {
                        if (reader[prop.Name] == DBNull.Value)
                        {
                            if (!prop.PropertyType.Name.StartsWith("Nullable") && !(prop.PropertyType.Name.Equals("String")))
                            {
                                throw new Exception(String.Format("The {0} property of {1} doestn´t allow null.", prop.Name, typeof(T).Name));
                            }
                            prop.SetValue(dato, null, null);
                        }
                        else
                        {
                            if (reader[prop.Name].GetType().Name.Equals(prop.PropertyType.Name))
                                prop.SetValue(dato, reader[prop.Name], null);
                            else
                            {
                                var desc = TypeDescriptor.GetConverter(prop.PropertyType).ConvertFrom(Convert.ToString(reader[prop.Name]));

                                prop.SetValue(dato, desc, null);
                            }
                        }
                    }
                }
                lista.Add((T)dato);

            }
        }
            #endregion

        #region QueryMethods

        public Tuple<IEnumerable<T1>, IEnumerable<T2>> MultipleQuery<T1, T2>()
        {
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>>(QueryAll<T1>(), QueryAll<T2>());
        }
        public Tuple<IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>> MultipleQuery<T1, T2, T3>()
        {
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>>(QueryAll<T1>(), QueryAll<T2>(), QueryAll<T3>());
        }
        //Expression<Func<T, object>> property

        public IEnumerable<T> Query<T>(Expression<Func<T, object>> columns = null, Expression<Func<T, object>> order = null, long take = 0, long skip = 0, Expression<Func<T, object>>  where = null, object[] args = null)
        {
            List<object> parameters = new List<object>();
            List<T> lista = new List<T>();
            CommaDelimitedStringCollection cols = new CommaDelimitedStringCollection();
            CommaDelimitedStringCollection ord = new CommaDelimitedStringCollection();
            StringBuilder whereBuilder = new StringBuilder();
            GetColumns<T>(columns, cols);
            GetColumns<T>(order, ord);
            GetWhere<T>(where, whereBuilder,parameters);
            return Query<T>(cols.ToString(), take, skip, ord.ToString(), whereBuilder.ToString(), parameters.ToArray());
        }
        private static void GetWhere<T>(Expression<Func<T, object>> where, StringBuilder builder,List<object> parameters )
        {
            
            if (where == null) return;
            if (builder == null) builder = new StringBuilder();
            List<Expression> expression = new List<Expression>();

            var info = (where.Body as UnaryExpression);
            var op = (info.Operand as BinaryExpression);
            
            var left = (op.Left as BinaryExpression);
            int i = 0;
            
            EvaluateWhereExpression(builder, parameters, expression, ref left, ref i);
            if (!string.IsNullOrWhiteSpace(builder.ToString()))
            {  
                builder.Insert(0,"(");
                builder.Append(")");
            }
            builder.Append(GetOperator(op.NodeType));
            var right = (op.Right as BinaryExpression);
            EvaluateWhereExpression(builder, parameters, expression, ref right, ref i);
          
          
        }
        private static void EvaluateWhereExpression(StringBuilder builder, List<object> parameters, List<Expression> expression, ref BinaryExpression left, ref int i)
        {
            while (left != null)
            {

                if ((left.Right as BinaryExpression) != null)
                {
                    if ((left.Right as BinaryExpression).Right.NodeType == ExpressionType.Constant)
                    {
                        parameters.Add(((left.Right as BinaryExpression).Right as ConstantExpression).Value);
                        if ((left.Right as BinaryExpression).Left.NodeType == ExpressionType.MemberAccess)
                        {
                            builder.Append(
                        String.Format(" {0} {1} :{2} {3}",
                        ((left.Right as BinaryExpression).Left as MemberExpression).Member.Name,
                        GetOperator((left.Right as BinaryExpression).NodeType),
                        i,
                        GetOperator(left.NodeType)));
                        }
                        else
                        {
                            builder.Append(
                        String.Format(" {0} {1} :{2} {3}",
                        (((left.Right as BinaryExpression).Left as UnaryExpression).Operand as MemberExpression).Member.Name,
                        GetOperator((left.Right as BinaryExpression).NodeType),
                        i,
                        GetOperator(left.NodeType)));
                        }
                        
                    }
                    else
                    {
                        parameters.Add((((left.Right as BinaryExpression).Right as UnaryExpression).Operand as ConstantExpression).Value);
                        builder.Append(
                        String.Format(" {0} {1} :{2} {3}",
                        ((left.Right as BinaryExpression).Left as MemberExpression).Member.Name,
                        GetOperator((left.Right as BinaryExpression).NodeType),
                        i,
                        GetOperator(left.NodeType)));
                    }


                    expression.Add(left.Right as BinaryExpression);
                }
                else if ((left.Right as MethodCallExpression) != null)
                {
                    parameters.Add(String.Format("'%{0}%'", ((left.Right as MethodCallExpression).Arguments[0] as ConstantExpression).Value));
                    // Obtener el valor de llamada ((left.Right as MethodCallExpression).Arguments[0] as ConstantExpression).Value
                    if ((left.Right as MethodCallExpression).Method.Name != "Contains") throw new Exception("Method not valid");
                    builder.Append(String.Format(" {0} Like (:{1}) {2}",
                        ((left.Right as MethodCallExpression).Object as MemberExpression).Member.Name,
                        i,
                        GetOperator(left.NodeType)));
                    expression.Add(left.Right as MethodCallExpression);
                }
                else
                {
                    //((left.Right as UnaryExpression).Operand as ConstantExpression).Value
                    parameters.Add(((left.Right as UnaryExpression).Operand as ConstantExpression).Value);
                    //parameters.Add((left.Right as ConstantExpression).Value);
                    builder.Append(
    String.Format(" {0} {1} :{2} ",
    ((left as BinaryExpression).Left as MemberExpression).Member.Name,
    GetOperator((left as BinaryExpression).NodeType),
    i));


                    expression.Add(left);
                }
                if (left.Left.NodeType == ExpressionType.Call)
                {
                    i++;
                    parameters.Add(String.Format("'%{0}%'", ((left.Left as MethodCallExpression).Arguments[0] as ConstantExpression).Value));
                    // Obtener el valor de llamada ((left.Right as MethodCallExpression).Arguments[0] as ConstantExpression).Value
                    if ((left.Left as MethodCallExpression).Method.Name != "Contains") throw new Exception("Method not valid");
                    builder.Append(String.Format(" {0} Like (:{1}) ",
                        ((left.Left as MethodCallExpression).Object as MemberExpression).Member.Name,
                        i));
                }
                left = ((left.Left as BinaryExpression));
                i++;
            }
        }
        private static string GetOperator(ExpressionType type)
        {
            switch (type)
            {
                case ExpressionType.Equal:
                    return "=";
                case ExpressionType.NotEqual:
                    return "!=";
                case ExpressionType.OrElse:
                    return " OR ";
                case ExpressionType.AndAlso:
                    return " AND ";
                case ExpressionType.GreaterThan:
                    return " > ";
                case ExpressionType.GreaterThanOrEqual:
                    return " >= ";
                case ExpressionType.LessThan:
                    return " < ";
                case ExpressionType.LessThanOrEqual:
                    return " <= ";
            }
            return string.Empty;
        }
        private static void GetColumns<T>(Expression<Func<T, object>> order, CommaDelimitedStringCollection cols)
        {
            if (order == null) return;
            var orders = order.Body.Type.GetProperties();
            foreach (var da in orders)
            {
                cols.Add(da.Name);
            }
        }

        private IEnumerable<T> Query<T>(string columns = "*", long take = 0, long skip = 0, string order = "", string where = "", object[] args = null)
        {
            List<T> lista = new List<T>();
            if (string.IsNullOrWhiteSpace(columns)) columns = "*";
            CheckConnection();
            using (var reader = GetQueryCommand<T>(columns, take, skip, order, where, args).ExecuteReader())
            {

                FetchReader<T>(lista, reader);
            }

            return lista;
        }


        public IEnumerable<T> QueryAll<T>() 
        {

            List<T> lista = new List<T>();
            using (connection = new OracleConnection(_connectionString))
            {
                connection.Open();
                using (var reader = new OracleCommand(String.Format("SELECT * FROM {0}.{1}", _schemmaName, typeof(T).Name), connection).ExecuteReader())
                {
                    FetchReader<T>(lista, reader);
                }
            }
            return lista;
        }
        #endregion
        #region Miembros de IDisposable

        public void Dispose()
        {
            if (connection != null)
            {
                connection.Close();
                connection.Dispose();
            }
        }

        #endregion

        public Enums.Mode ConnectionMode { get; set; }

        private Dictionary<Mapper, Type> Mapping;
        // 2.0
        public void ConfigureMapping<T>(IGenericMapper<T> mapping)
        {
            if (Mapping == null) Mapping = new Dictionary<Mapper, Type>();
            Mapping.Add((Mapper)mapping, typeof(T));
        }
        public void AutoConfigureMappings()
        {
           var assembly =  Assembly.Load(this.GetType().Assembly.FullName);
           assembly.GetTypes().
              Where(type => {
                    if (type.BaseType == null) return false;
                    if (type.BaseType.BaseType == null) return false;
                    if (type.BaseType.BaseType.Equals(typeof(Mapper)))
                        return true;
                    return false;
              })
              .ToList()
              .ForEach(type =>
                  {
                      var objData = type.GetConstructor(new Type[0]).Invoke(new object[0]);
                      if (Mapping == null) Mapping = new Dictionary<Mapper, Type>();
                      Mapping.Add(
                                  (Mapper)objData, (type.InvokeMember("GetMappedType",BindingFlags.InvokeMethod,null,objData,null) as Type)
                                  );
                  });

        }


    }
}

