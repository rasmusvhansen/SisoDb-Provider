﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SisoDb.Querying
{
    [Serializable]
    internal class SqlQuery : ISqlQuery
    {
        private readonly ReadOnlyCollection<IQueryParameter> _parameters;

        public string Sql { get; private set; }

        public IList<IQueryParameter> Parameters
        {
            get { return _parameters; }
        }

        internal SqlQuery(string sql, IEnumerable<IQueryParameter> parameters)
        {
            sql.AssertNotNullOrWhiteSpace("sql");
            parameters.AssertNotNull("parameters");

            Sql = sql;

            _parameters = new ReadOnlyCollection<IQueryParameter>(parameters.Distinct().ToList());
        }
    }
}