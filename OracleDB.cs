using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Configuration;
using Oracle.DataAccess.Client;
using System.ComponentModel;
using System.Windows.Forms;

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
        private List<Base> insertCommands;
        private List<Base> updateCommands;
        private List<Base> deleteCommands;
        public OracleDB(string schemmaName,Enums.Mode connectionMode = Enums.Mode.Normal)
        {
            Init(schemmaName, connectionMode);
            _connectionString = System.Configuration.ConfigurationManager.ConnectionStrings[System.Configuration.ConfigurationManager.ConnectionStrings.Count - 1].ConnectionString;
        }

        private void Init(string schemmaName, Enums.Mode connectionMode)
        {
            _schemmaName = schemmaName;
            ConnectionMode = connectionMode;
            insertCommands = new List<Base>();
            updateCommands = new List<Base>();
            deleteCommands = new List<Base>();
        }

        public OracleDB(string connectionStringName, string schemmaName, Enums.Mode connectionMode = Enums.Mode.Normal)
        {

             Init(schemmaName, connectionMode);
            _connectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
        }

        public void Insert<T>(T entity) where T : Base 
        {
            using (var command = GetInsertCommand(entity))
            {
                ExecuteMethod(command);
            }
        }


        public void Update<T>(T entity) where T : Base 
        {
            using (var command = GetUpdateCommand(entity))
            {
                ExecuteMethod(command);
            }
        }
        public void Delete<T>(T entity) where T : Base 
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
                OracleCommand command = GetInsertCommand(element);
                command.Connection = connection;
                command.ExecuteNonQuery();
            }
        }

        private void ExecuteUpdates()
        {
            foreach (var element in updateCommands)
            {
                OracleCommand command = GetInsertCommand(element);
                command.Connection = connection;
                command.ExecuteNonQuery();
            }
        }

        private void ExecuteInserts()
        {
            foreach (var element in insertCommands)
            {
                 OracleCommand command = GetInsertCommand(element);
                command.Connection = connection;
                command.ExecuteNonQuery();
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
                insertCommands.Add((Base)entity);
                return null;
            }

            OracleCommand cmd = new OracleCommand();
            StringBuilder builder = new StringBuilder();
            StringBuilder columns = new StringBuilder();
            StringBuilder parameters = new StringBuilder();
            
            foreach (var prop in entity.GetType().GetProperties())
            {
                columns.Append(String.Format("{0},", prop.Name));
               
                var seq = prop.GetCustomAttributes(false).Where(a => a is SequenceAttribute).FirstOrDefault();
                var defaultValue = prop.GetCustomAttributes(false).Where(a => a is DefaultValueAttribute).FirstOrDefault();
                 if (seq!=null)
                 {
                     parameters.Append(String.Format("{0}.{1}.NextVal,",_schemmaName, (seq as SequenceAttribute).SequenceName));
                 }
                 else
                 {
                     if (defaultValue == null)
                     {
                         parameters.Append(String.Format(":{0},", prop.Name));
                         if (prop.PropertyType.Name.StartsWith("Nullable") && prop.GetValue(entity, null) == null)
                         {
                             cmd.Parameters.Add(new OracleParameter(prop.Name, DBNull.Value));
                         }
                         else
                         {
                             cmd.Parameters.Add(new OracleParameter(prop.Name, prop.GetValue(entity, null)));
                         }
                     }
                     else
                     {
                         parameters.Append(String.Format("{0},", (defaultValue as DefaultValueAttribute).Value.ToString()));
                     }
                 }
                    
            }
            columns.Remove(columns.Length - 1, 1);
            parameters.Remove(parameters.Length - 1, 1);

            builder.Append(String.Format("INSERT INTO {0}.{1} ({2}) VALUES ({3})",_schemmaName, entity.GetType().Name, columns, parameters));
            cmd.CommandText = builder.ToString();
            return cmd;

        }

        private OracleCommand GetUpdateCommand(object entity)
        {
            if (ConnectionMode == Enums.Mode.UnitOfWork && !insertCommands.Where(a => a.Equals(entity)).Any())
            {
                updateCommands.Add((Base)entity);
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
                    if (prop.PropertyType.Name.StartsWith("Nullable") && prop.GetValue(entity, null)==null)
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
                deleteCommands.Add((Base)entity);
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
        private OracleCommand GetQueryCommand<T>(string columns, long take, long skip, string order, string where, object[] args) where T : Base 
        {
            var command = new OracleCommand(GetQueryText<T>(columns, take, skip,order, where, args), connection);
            if (args != null)
            {
                for (int i = 0; i < args.Count(); i++)
                {
                    command.Parameters.Add(new OracleParameter(i.ToString(), args[i]));
                }
            }
            return command;
        }
        private string GetQueryText<T>(string columns, long take, long skip,string order, string where, object[] args) where T : Base
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(String.Format("SELECT {1} FROM (select row_number() over (order by 1 DESC) r,{2}.{1} from  {0}.{2})", _schemmaName, columns, typeof(T).Name));

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
            if (!string.IsNullOrWhiteSpace(where))
            {
                if (builder.ToString().Contains("WHERE"))
                    builder.Append(" AND ");
                else
                    builder.Append(" WHERE ");
                builder.Append(where);
            }
            if (!string.IsNullOrWhiteSpace(order))
            {
                builder.Append(String.Format(" ORDER BY {0}", order));
            }
            return builder.ToString();
        }
        private static void FetchReader<T>(List<T> lista, OracleDataReader reader) where T : Base 
        {
            var converter = new TypeConverter();
            while (reader.Read())
            {
                var dato = typeof(T).GetConstructor(new Type[0]).Invoke(new object[0]);
                foreach (var prop in dato.GetType().GetProperties())
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
                lista.Add((T)dato);

            }
        }
        #endregion

        #region QueryMethods

        public Tuple<IEnumerable<T1>, IEnumerable<T2>> MultipleQuery<T1, T2>() 
            where T1 : Base 
            where T2 : Base
        {
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>>(QueryAll<T1>(), QueryAll<T2>());
        }
        public Tuple<IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>> MultipleQuery<T1, T2, T3>()
            where T1 : Base
            where T2 : Base
            where T3 : Base 
        {
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>>(QueryAll<T1>(), QueryAll<T2>(), QueryAll<T3>()); 
        }
        public IEnumerable<T> Query<T>(string columns = "*", long take = 0, long skip = 0, string order = "", string where = "", object[] args = null) where T : Base 
        {
            List<T> lista = new List<T>();
            if (string.IsNullOrWhiteSpace(columns)) columns = "*";
            CheckConnection();
                using (var reader = GetQueryCommand<T>(columns, take, skip,order, where, args).ExecuteReader())
                {

                    FetchReader<T>(lista, reader);
                }
          
            return lista;
        }


        public IEnumerable<T> QueryAll<T>() where T : Base 
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
    }
}

