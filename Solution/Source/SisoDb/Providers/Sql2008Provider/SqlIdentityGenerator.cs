﻿using SisoDb.Core;
using SisoDb.Structures.Schemas;

namespace SisoDb.Providers.Sql2008Provider
{
    public class SqlIdentityGenerator : IIdentityGenerator
    {
        private readonly ISqlDbClient _dbClient;

        public SqlIdentityGenerator(ISqlDbClient dbClient)
        {
            _dbClient = dbClient.AssertNotNull("dbClient");
        }

        public int CheckOutAndGetSeed(IStructureSchema structureSchema, int numOfIds)
        {
            return _dbClient.CheckOutAndGetNextIdentity(structureSchema.Hash, numOfIds);
        }
    }
}