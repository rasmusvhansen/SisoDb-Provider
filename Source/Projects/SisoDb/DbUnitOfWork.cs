using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using EnsureThat;
using NCore;
using PineCone.Structures;
using PineCone.Structures.Schemas;
using SisoDb.Core.Expressions;
using SisoDb.Dac;
using SisoDb.Resources;
using SisoDb.Structures;

namespace SisoDb
{
	public abstract class DbUnitOfWork : DbQueryEngine, IUnitOfWork
	{
		protected const int MaxUpdateManyBatchSize = 1000;

		protected IDbClient DbClientNonTransactional;

		protected readonly IIdentityStructureIdGenerator IdentityStructureIdGenerator;

		protected DbUnitOfWork(
			IDbDatabase db, 
			IDbClient dbClientTransactional,
			IDbClient dbClientNonTransactional,
			IIdentityStructureIdGenerator identityStructureIdGenerator)
			: base(db, dbClientTransactional)
		{
			Ensure.That(dbClientNonTransactional, "dbClientNonTransactional").IsNotNull();
			Ensure.That(identityStructureIdGenerator, "identityStructureIdGenerator").IsNotNull();

			DbClientNonTransactional = dbClientNonTransactional;
			IdentityStructureIdGenerator = identityStructureIdGenerator;
		}

		public override void Dispose()
		{
			base.Dispose();

			if (DbClientNonTransactional != null)
			{
				DbClientNonTransactional.Dispose();
				DbClientNonTransactional = null;
			}

			GC.SuppressFinalize(this);
		}

		protected override void UpsertStructureSet(IStructureSchema structureSchema)
		{
			Db.SchemaManager.UpsertStructureSet(structureSchema, DbClientNonTransactional);
		}

		public virtual void Commit()
		{
			DbClient.Flush();
		}

		public virtual void Insert<T>(T item) where T : class
		{
			var structureSchema = GetStructureSchema<T>();
			UpsertStructureSet(structureSchema);

			var structureBuilder = Db.StructureBuilders.ForInserts(structureSchema, IdentityStructureIdGenerator);

			var structure = structureBuilder.CreateStructure(item, structureSchema);

			var bulkInserter = Db.ProviderFactory.GetStructureInserter(DbClient);
			bulkInserter.Insert(structureSchema, new[] { structure });
		}

		public virtual void InsertJson<T>(string json) where T : class
		{
			Insert(Db.Serializer.Deserialize<T>(json));
		}

		public virtual void InsertMany<T>(IList<T> items) where T : class
		{
			var structureSchema = GetStructureSchema<T>();
			UpsertStructureSet(structureSchema);

			var structureBuilder = Db.StructureBuilders.ForInserts(structureSchema, IdentityStructureIdGenerator);

			var bulkInserter = Db.ProviderFactory.GetStructureInserter(DbClient);
			bulkInserter.Insert(structureSchema, structureBuilder.CreateStructures(items, structureSchema));
		}

		public virtual void InsertManyJson<T>(IEnumerable<string> json) where T : class
		{
			InsertMany(Db.Serializer.DeserializeMany<T>(json).ToList());
		}

		public virtual void Update<T>(T item) where T : class
		{
			var structureSchema = GetStructureSchema<T>();
			UpsertStructureSet(structureSchema);

			var structureBuilder = Db.StructureBuilders.ForUpdates(structureSchema);

			var updatedStructure = structureBuilder.CreateStructure(item, structureSchema);

			var existingItem = DbClient.GetJsonById(updatedStructure.Id, structureSchema);

			if (string.IsNullOrWhiteSpace(existingItem))
				throw new SisoDbException(ExceptionMessages.UnitOfWork_NoItemExistsForUpdate.Inject(updatedStructure.Name, updatedStructure.Id.Value));

			DeleteById(structureSchema, updatedStructure.Id);

			var bulkInserter = Db.ProviderFactory.GetStructureInserter(DbClient);
			bulkInserter.Insert(structureSchema, new[] { updatedStructure });
		}

		public virtual bool UpdateMany<T>(Func<T, UpdateManyModifierStatus> modifier, Expression<Func<T, bool>> expression = null) where T : class
		{
			var spec = UpdateManySpec.Create<T>(StructureSchemas);
			UpsertStructureSet(spec.NewSchema);

			IStructureId deleteIdFrom = null, deleteIdTo = null;
			var keepQueue = new List<T>(MaxUpdateManyBatchSize);

			var structureBuilder = Db.StructureBuilders.ForUpdates(spec.NewSchema);
			var structureInserter = Db.ProviderFactory.GetStructureInserter(DbClient);

			var query = spec.BuildQuery(Db, spec.NewSchema, expression);
			foreach (var structure in Query<T>(query))
			{
				var structureId = spec.NewSchema.IdAccessor.GetValue(structure);
				var status = modifier.Invoke(structure);
				if (status == UpdateManyModifierStatus.Abort)
					return false;
				
				deleteIdFrom = deleteIdFrom ?? structureId;
				deleteIdTo = structureId;

				if (status == UpdateManyModifierStatus.Keep)
				{
					keepQueue.Add(structure);
					if (keepQueue.Count < MaxUpdateManyBatchSize)
						continue;

					DbClient.DeleteWhereIdIsBetween(deleteIdFrom, deleteIdTo ?? deleteIdFrom, spec.NewSchema);
					deleteIdFrom = null;
					deleteIdTo = null;

					structureInserter.Insert(spec.NewSchema, structureBuilder.CreateStructures(keepQueue, spec.NewSchema));
					keepQueue.Clear();
				}
			}

			if (keepQueue.Count > 0)
			{
				if (deleteIdFrom != null)
					DbClient.DeleteWhereIdIsBetween(deleteIdFrom, deleteIdTo ?? deleteIdFrom, spec.NewSchema);

				structureInserter.Insert(spec.NewSchema, structureBuilder.CreateStructures(keepQueue, spec.NewSchema));
				keepQueue.Clear();
			}

			return true;
		}

		public virtual bool UpdateMany<TOld, TNew>(Func<TOld, TNew, UpdateManyModifierStatus> modifier, Expression<Func<TOld, bool>> expression = null)
			where TOld : class
			where TNew : class
		{
			var spec = UpdateManySpec.Create<TOld, TNew>(StructureSchemas);
			if (spec.TypesAreIdentical)
				throw new SisoDbException(ExceptionMessages.UnitOfWork_UpdateMany_TOld_TNew_SameType);

			UpsertStructureSet(spec.NewSchema);

			Func<IQuery, IEnumerable<string>> queryInvoker;
			
			if (spec.IsUpdatingSameSchema)
			{
				StructureSchemas.RemoveSchema(spec.OldType);
				Db.SchemaManager.RemoveFromCache(spec.OldSchema);
				
				queryInvoker = QueryAsJson<TNew>;
			}
			else
			{
				UpsertStructureSet(spec.OldSchema);

				queryInvoker = QueryAsJson<TOld>;
			}

			IStructureId deleteIdFrom = null, deleteIdTo = null;
			var keepQueue = new List<TNew>(MaxUpdateManyBatchSize);

			var structureBuilder = Db.StructureBuilders.ForUpdates(spec.NewSchema);
			var structureInserter = Db.ProviderFactory.GetStructureInserter(DbClient);

			var query = spec.BuildQuery(Db, spec.OldSchema, expression);
			foreach (var oldStructureJson in queryInvoker(query))
			{
				var oldStructure = Db.Serializer.Deserialize<TOld>(oldStructureJson);
				var newStructure = Db.Serializer.Deserialize<TNew>(oldStructureJson);
				var structureId = spec.OldSchema.IdAccessor.GetValue(oldStructure);
				
				var status = modifier.Invoke(oldStructure, newStructure);
				if (status == UpdateManyModifierStatus.Abort)
					return false;

				deleteIdFrom = deleteIdFrom ?? structureId;
				deleteIdTo = structureId;

				if (status == UpdateManyModifierStatus.Keep)
				{
					keepQueue.Add(newStructure);
					if (keepQueue.Count < MaxUpdateManyBatchSize)
						continue;

					DbClient.DeleteWhereIdIsBetween(deleteIdFrom, deleteIdTo ?? deleteIdFrom, spec.OldSchema);
					deleteIdFrom = null;
					deleteIdTo = null;

					structureInserter.Insert(spec.NewSchema, structureBuilder.CreateStructures(keepQueue, spec.NewSchema));
					keepQueue.Clear();
				}
			}

			if (keepQueue.Count > 0)
			{
				if (deleteIdFrom != null)
					DbClient.DeleteWhereIdIsBetween(deleteIdFrom, deleteIdTo ?? deleteIdFrom, spec.OldSchema);

				structureInserter.Insert(spec.NewSchema, structureBuilder.CreateStructures(keepQueue, spec.NewSchema));
				keepQueue.Clear();
			}

			if (!spec.IsUpdatingSameSchema)
			{
				StructureSchemas.RemoveSchema(spec.OldType);
				Db.SchemaManager.DropStructureSet(spec.OldSchema, DbClient);
			}

			return true;
		}

		public virtual void DeleteById<T>(object id) where T : class
		{
			var structureSchema = GetStructureSchema<T>();
			UpsertStructureSet(structureSchema);

			DeleteById(structureSchema, StructureId.ConvertFrom(id));
		}

		private void DeleteById(IStructureSchema structureSchema, IStructureId structureId)
		{
			DbClient.DeleteById(structureId, structureSchema);
		}

		public virtual void DeleteByIds<T>(params object[] ids) where T : class
		{
			Ensure.That(ids, "ids").HasItems();

			var structureSchema = GetStructureSchema<T>();
			UpsertStructureSet(structureSchema);

			DbClient.DeleteByIds(ids.Select(StructureId.ConvertFrom), structureSchema);
		}

		public virtual void DeleteByIdInterval<T>(object idFrom, object idTo) where T : class
		{
			var structureSchema = GetStructureSchema<T>();

			if (!structureSchema.IdAccessor.IdType.IsIdentity())
				throw new SisoDbException(ExceptionMessages.SisoDbNotSupportedByProviderException.Inject(Db.ProviderFactory.ProviderType, ExceptionMessages.UnitOfWork_DeleteByIdInterval_WrongIdType));

			UpsertStructureSet(structureSchema);

			DbClient.DeleteWhereIdIsBetween(StructureId.ConvertFrom(idFrom), StructureId.ConvertFrom(idTo), structureSchema);
		}

		public virtual void DeleteByQuery<T>(Expression<Func<T, bool>> expression) where T : class
		{
			Ensure.That(expression, "expression").IsNotNull();

			var structureSchema = GetStructureSchema<T>();
			UpsertStructureSet(structureSchema);

			var queryBuilder = Db.ProviderFactory.GetQueryBuilder<T>(StructureSchemas);
			queryBuilder.Where(expression);

			var sql = QueryGenerator.GenerateQueryReturningStrutureIds(queryBuilder.Build());
			DbClient.DeleteByQuery(sql, structureSchema);
		}

		private class UpdateManySpec
		{
			public readonly IStructureSchema OldSchema;
			public readonly IStructureSchema NewSchema;
			public readonly bool IsUpdatingSameSchema;
			public readonly Type OldType;
			public readonly bool TypesAreIdentical;

			private UpdateManySpec(IStructureSchema oldSchema, Type oldType, IStructureSchema newSchema, Type newType)
			{
				OldSchema = oldSchema;
				OldType = oldType;
				NewSchema = newSchema;

				IsUpdatingSameSchema = OldSchema.Name.Equals(NewSchema.Name, StringComparison.InvariantCultureIgnoreCase);
				TypesAreIdentical = oldType.Equals(newType);
			}

			internal static UpdateManySpec Create<T>(IStructureSchemas structureSchemas) where T : class
			{
				return Create<T, T>(structureSchemas);
			}

			internal static UpdateManySpec Create<TOld, TNew>(IStructureSchemas structureSchemas)
				where TOld : class
				where TNew : class
			{
				var oldType = typeof(TOld);
				var oldSchema = structureSchemas.GetSchema<TOld>();
				structureSchemas.RemoveSchema(oldType);

				var newType = typeof(TNew);
				var newSchema = structureSchemas.GetSchema<TNew>();
				structureSchemas.RemoveSchema(newType);

				return new UpdateManySpec(oldSchema, oldType, newSchema, newType);
			}

			internal IQuery BuildQuery<T>(IDbDatabase db, IStructureSchema structureSchema, Expression<Func<T, bool>> expression = null) where T : class
			{
				var queryBuilder = db.ProviderFactory.GetQueryBuilder<T>(db.StructureSchemas);
				if (expression != null)
				{
					queryBuilder.Where(expression);
					queryBuilder.OrderBy(ExpressionUtils.GetMemberExpression<T>(structureSchema.IdAccessor.Path));
				}

				return queryBuilder.Build();
			}
		}
	}
}