using NUnit.Framework;
using SisoDb.Lambdas.Processors;
using SisoDb.Querying;

namespace SisoDb.Tests.UnitTests.Lambdas.ParsedSortingLambdaSqlProcessorTests
{
    [TestFixture]
    public class ParsedSortingLambdaSqlProcessorTests : ParsedSortingLambdaSqlProcessorTestBase
    {
        [Test]
        public void Process_WhenOneMemberSortingImplicitAscending_SqlForOneMemberAscending()
        {
            var parsedLambda = CreateParsedLambda<MyItem>(i => i.Int1);

            var processor = new ParsedSortingLambdaSqlProcessor(new MemberNameGeneratorFake());
            var query = processor.Process(parsedLambda);

            const string expectedSql = "si.[Int1] Asc";
            Assert.AreEqual(expectedSql, query.Sql);
        }

        [Test]
        public void Process_WhenOneNestedMemberSortingImplicitAscending_SqlForOneMemberAscending()
        {
            var parsedLambda = CreateParsedLambda<MyItem>(i => i.NestedItem.SuperNestedItem.Int1);

            var processor = new ParsedSortingLambdaSqlProcessor(new MemberNameGeneratorFake());
            var query = processor.Process(parsedLambda);

            const string expectedSql = "si.[NestedItem.SuperNestedItem.Int1] Asc";
            Assert.AreEqual(expectedSql, query.Sql);
        }

        [Test]
        public void Process_WhenOneMemberSortingExplicitAscending_SqlWithOneMemberAscending()
        {
            var parsedLambda = CreateParsedLambda<MyItem>(i => i.Int1.Asc());

            var processor = new ParsedSortingLambdaSqlProcessor(new MemberNameGeneratorFake());
            var query = processor.Process(parsedLambda);

            const string expectedSql = "si.[Int1] Asc";
            Assert.AreEqual(expectedSql, query.Sql);
        }

        [Test]
        public void Process_WhenOneMemberSortingExplicitDescending_SqlWithOneMemberDescending()
        {
            var parsedLambda = CreateParsedLambda<MyItem>(i => i.Int1.Desc());

            var processor = new ParsedSortingLambdaSqlProcessor(new MemberNameGeneratorFake());
            var query = processor.Process(parsedLambda);

            const string expectedSql = "si.[Int1] Desc";
            Assert.AreEqual(expectedSql, query.Sql);
        }

        [Test]
        public void Process_WhenTwoMembersWhereOneIsImplicitAscendingAndOneExplicitDescending_SqlWithOneMemberAscendingAndOneDescending()
        {
            var parsedLambda = CreateParsedLambda<MyItem>(i => i.Int1.Desc(), i => i.DateTime1);

            var processor = new ParsedSortingLambdaSqlProcessor(new MemberNameGeneratorFake());
            var query = processor.Process(parsedLambda);

            const string expectedSql = "si.[Int1] Desc, si.[DateTime1] Asc";
            Assert.AreEqual(expectedSql, query.Sql);
        }
    }
}