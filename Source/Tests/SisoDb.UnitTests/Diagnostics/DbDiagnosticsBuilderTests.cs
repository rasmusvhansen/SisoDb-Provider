﻿using System;
using Moq;
using NUnit.Framework;
using SisoDb.Diagnostics.Builders;
using SisoDb.ServiceStack;
using SisoDb.Sql2012;
using SisoDb.Structures.Schemas;
using SisoDb.Structures.Schemas.Builders;

namespace SisoDb.UnitTests.Diagnostics
{
    [TestFixture]
    public class DbDiagnosticsBuilderTests : UnitTestBase
    {
        [Test]
        public void Build()
        {
            var structureSchemas = new StructureSchemas(new StructureTypeFactory(), new AutoStructureSchemaBuilder());
            structureSchemas.GetSchema<MyDummy>();
            var dbFake = new Mock<ISisoDatabase>();
            dbFake.SetupGet(f => f.Name).Returns("UnitTestDb");
            dbFake.Setup(f => f.ConnectionInfo).Returns(new Sql2012ConnectionInfo("data source=.;initial catalog=foo;integrated security=true;"));
            dbFake.Setup(f => f.Settings).Returns(DbSettings.CreateDefault());
            dbFake.Setup(f => f.Serializer).Returns(new ServiceStackSisoSerializer());
            dbFake.Setup(f => f.StructureSchemas).Returns(structureSchemas);

            var dbDiagnostics = new DbDiagnosticsBuilder(dbFake.Object);
            var info = dbDiagnostics.Build();

            JsonApprovals.VerifyAsJson(info);
        }

        private class MyDummy
        {
            public Guid StructureId { get; set; }
            public DateTime TimeStamp { get; set; }
            public Guid ConcurrencyToken { get; set; }
            public string StringValue { get; set; }
        }
    }
}