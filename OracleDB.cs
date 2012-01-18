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
    public class OracleDB : IDisposable
    {

        public OracleDB(string schemmaName)
        {
            _schemmaName = schemmaName;
            _connectionString = ConfigurationManager.ConnectionStrings[ConfigurationManager.ConnectionStrings.Count - 1].ConnectionString;
        }

        public OracleDB(string connectionStringName, string schemmaName)
        {
            _connectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
            _schemmaName = schemmaName;
        }
        private OracleConnection connection;
        private string _connectionString;
        private string _schemmaName;
        public void Insert<T>(T entity)
        {
            CheckConnection();
            using (var command = GetInsertCommand(entity))
            {
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
        }
        public void Update<T>(T entity)
        {
            CheckConnection();

            using (var command = GetUpdateCommand(entity))
            {
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

        }
        public void Delete<T>(T entity)
        {
            CheckConnection();


            using (var command = GetDeleteCommand(entity))
            {
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

        }

       
        #region PrivateMethods
        private void CheckConnection()
        {
            if (connection == null) connection = new Oracle.DataAccess.Client.OracleConnection(_connectionString);
            if (connection.State != System.Data.ConnectionState.Open)
                connection.Open();
        }
        private OracleCommand GetInsertCommand(object entity)
        {
            OracleCommand cmd = new OracleCommand();
            StringBuilder builder = new StringBuilder();
            StringBuilder columns = new StringBuilder();
            StringBuilder parameters = new StringBuilder();

            foreach (var prop in entity.GetType().GetProperties())
            {
                columns.Append(String.Format("{0},", prop.Name));
                parameters.Append(String.Format(":{0},", prop.Name));
                cmd.Parameters.Add(new OracleParameter(prop.Name, prop.GetValue(entity, null)));
            }
            columns.Remove(columns.Length - 1, 1);
            parameters.Remove(parameters.Length - 1, 1);

            builder.Append(String.Format("INSERT INTO {0} ({1}) VALUES ({2})", entity.GetType().Name, columns, parameters));
            cmd.CommandText = builder.ToString();
            return cmd;

        }

        private OracleCommand GetUpdateCommand(object entity)
        {
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
                    columns.Append(String.Format("{0} = :{0},", prop.Name));

                cmd.Parameters.Add(new OracleParameter(prop.Name, prop.GetValue(entity, null)));
            }
            columns.Remove(columns.Length - 1, 1);
            where.Remove(where.Length - 3, 3);

            builder.Append(String.Format("UPDATE {0} SET {1} WHERE {2}", entity.GetType().Name, columns, where));
            cmd.CommandText = builder.ToString();
            return cmd;

        }

        private OracleCommand GetDeleteCommand(object entity)
        {
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

            builder.Append(String.Format("DELETE FROM {0} WHERE {1}", entity.GetType().Name, where));
            cmd.CommandText = builder.ToString();
            return cmd;

        }
        private OracleCommand GetQueryCommand<T>(string columns, long take, long skip,string order, string where, object[] args)
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
        private string GetQueryText<T>(string columns, long take, long skip,string order, string where, object[] args)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(String.Format("SELECT {1} FROM (select row_number() over (order by 1 DESC) r,{2}.{1} from  {0}.{2})", _schemmaName, columns, typeof(T).Name));

            if (skip > 0 && take == 0)
            {
                builder.Append(String.Format(" WHERE r >{0}", skip));
            }
            if (take > 0 && skip == 0)
            {
                builder.Append(String.Format(" WHERE r <{0}", skip));
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
        private static void FetchReader<T>(List<T> lista, OracleDataReader reader)
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
        {
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>>(QueryAll<T1>(), QueryAll<T2>());
        }
        public Tuple<IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>> MultipleQuery<T1, T2,T3>()
        {
            return new Tuple<IEnumerable<T1>, IEnumerable<T2>, IEnumerable<T3>>(QueryAll<T1>(), QueryAll<T2>(), QueryAll<T3>());
        }
        public IEnumerable<T> Query<T>(string columns = "*", long take = 0, long skip = 0,string order = "", string where = "", object[] args = null)
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
    }
}

