﻿using System;
using SisoDb.Providers.Sql2008;

namespace SisoDb.Providers.SqlAzure
{
    public class SqlAzureDatabase : SqlDatabase
    {
        internal SqlAzureDatabase(ISisoConnectionInfo connectionInfo) : base(connectionInfo)
        {  
        }

        public override void EnsureNewDatabase()
        {
            throw new NotSupportedException();
        }

        public override void CreateIfNotExists()
        {
            throw new NotSupportedException();
        }

        public override void DeleteIfExists()
        {
            throw new NotSupportedException();
        }

        public override bool Exists()
        {
            throw new NotSupportedException();
        }
    }
}