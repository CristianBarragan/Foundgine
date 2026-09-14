using System.Text.Json;
using System.Collections;
using System.Data;
using System.Data.Common;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Semantic.Planning.Mutation;
using Foundgine.Core.Execution;
using Foundgine.Providers.Storage.Sql.Mutation;
using Foundgine.Providers.Storage.Sql.Mutation.Postgres;
using Npgsql;
using Xunit;
using ExecutionContext = Foundgine.Core.Execution.ExecutionContext;

namespace Foundgine.E2E.Tests;

#pragma warning disable CS8765 // DbParameter uses nullable setter annotations that differ across target framework versions.

public sealed class PostgresCorrelationContractTests
{
    [Fact]
    public void BatchedPostgresCompilerExposesExecutionIrEntryPoint()
    {
        var method = typeof(PostgresBatchedMutationCompiler).GetMethod(
            nameof(PostgresBatchedMutationCompiler.Compile),
            new[] { typeof(Foundgine.Core.Execution.Mutation.ExecutionMutationIR) });

        Assert.NotNull(method);
    }

    [Fact]
    public void BatchedPostgresCompilerExposesSafeTryCompileEntryPoint()
    {
        var method = typeof(PostgresBatchedMutationCompiler).GetMethod(
            nameof(PostgresBatchedMutationCompiler.TryCompile),
            new[] { typeof(Foundgine.Core.Execution.Mutation.ExecutionMutationIR) });

        Assert.NotNull(method);
    }

    [Fact]
    public void BatchedCreateUsesExplicitCorrelationKeyAcrossInputAndReturningCtes()
    {
        var entityId = new EntityId(1);
        var idColumn = new ColumnId(1);
        var nameColumn = new ColumnId(2);
        var idField = new FieldId(1);
        var nameField = new FieldId(2);

        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            entityId,
            "Customer",
            [
                new ColumnMetadata(idColumn, "Id"),
                new ColumnMetadata(nameColumn, "Name")
            ],
            Fields:
            [
                new FieldMetadata(idField, "Id", typeof(long), new ColumnReference(entityId, idColumn)),
                new FieldMetadata(nameField, "Name", typeof(string), new ColumnReference(entityId, nameColumn))
            ],
            PrimaryKey: new ColumnReference(entityId, idColumn)));

        var entity = new MutationEntitySchema(
            entityId,
            "Customer",
            new HashSet<ColumnId> { idColumn, nameColumn },
            new Dictionary<FieldId, ColumnId?>
            {
                [idField] = idColumn,
                [nameField] = nameColumn
            },
            idColumn);

        var operations = new[]
        {
            new MutationOperation(
                entity, MutationKind.Create,
                [new MutationFieldValue(idColumn, 101L), new MutationFieldValue(nameColumn, "Alice")],
                null, null, [idField, nameField]),
            new MutationOperation(
                entity, MutationKind.Create,
                [new MutationFieldValue(idColumn, 102L), new MutationFieldValue(nameColumn, "Bob")],
                null, null, [idField, nameField])
        };

        var plan = new MutationBatchPlan(operations, []);
        var sql = new PostgresBatchedMutationCompiler(metadata).Compile(plan).CommandText;

        Assert.Contains("g0_input AS (SELECT * FROM g0_resolved)", sql, StringComparison.Ordinal);
        Assert.Contains("__fg_corr", sql, StringComparison.Ordinal);
        Assert.Contains("g0_created AS (\n  MERGE INTO", sql, StringComparison.Ordinal);
        Assert.Contains("WHEN NOT MATCHED THEN INSERT (\"Id\", \"Name\") VALUES (r.\"Id\", r.\"Name\")", sql,
            StringComparison.Ordinal);
        Assert.Contains("USING g0_input r ON FALSE", sql, StringComparison.Ordinal);
        Assert.Contains("RETURNING r.__fg_corr, t.\"Id\" AS \"r_1\", t.\"Name\" AS \"r_2\"", sql,
            StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN g0_created f ON", sql, StringComparison.Ordinal);
        Assert.Contains("jsonb_build_object('__fg_corr', f.__fg_corr)", sql, StringComparison.Ordinal);
        Assert.Contains("ORDER BY __grp, CASE WHEN __row ? '__fg_corr'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void GeneratedIdentityCreateUsesCompilerOwnedCorrelationCarrier()
    {
        var entityId = new EntityId(7);
        var idColumn = new ColumnId(1);
        var nameColumn = new ColumnId(2);
        var idField = new FieldId(1);
        var nameField = new FieldId(2);

        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            entityId,
            "Customer",
            [new ColumnMetadata(idColumn, "Id"), new ColumnMetadata(nameColumn, "Name")],
            Fields:
            [
                new FieldMetadata(idField, "Id", typeof(long), new ColumnReference(entityId, idColumn)),
                new FieldMetadata(nameField, "Name", typeof(string), new ColumnReference(entityId, nameColumn))
            ],
            PrimaryKey: new ColumnReference(entityId, idColumn)));

        var entity = new MutationEntitySchema(
            entityId,
            "Customer",
            new HashSet<ColumnId> { idColumn, nameColumn },
            new Dictionary<FieldId, ColumnId?>
                { [idField] = idColumn, [nameField] = nameColumn },
            idColumn);

        // Id is intentionally omitted: PostgreSQL owns generation. The two
        // logical operations even have the same user value, proving that no
        // target column is being borrowed as a correlation key.
        var plan = new MutationBatchPlan(
        [
            new MutationOperation(entity, MutationKind.Create,
                [new MutationFieldValue(nameColumn, "same")], null, null, [idField, nameField]),
            new MutationOperation(entity, MutationKind.Create,
                [new MutationFieldValue(nameColumn, "same")], null, null, [idField, nameField])
        ], []);

        var sql = new PostgresBatchedMutationCompiler(metadata).Compile(plan).CommandText;

        Assert.Contains("MERGE INTO \"Customer\" t", sql, StringComparison.Ordinal);
        Assert.Contains("USING g0_input r ON FALSE", sql, StringComparison.Ordinal);
        Assert.Contains("RETURNING r.__fg_corr", sql, StringComparison.Ordinal);
        Assert.Contains("t.\"Id\" AS \"r_1\"", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("g0_keys", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("JOIN \"Customer\" t ON", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void BatchedExecutorCorrelatesRowsWhenDatabaseReturnsThemOutOfOrder()
    {
        var entityId = new EntityId(1);
        var idColumn = new ColumnId(1);
        var nameColumn = new ColumnId(2);
        var idField = new FieldId(1);
        var nameField = new FieldId(2);

        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            entityId,
            "Customer",
            [
                new ColumnMetadata(idColumn, "Id"),
                new ColumnMetadata(nameColumn, "Name")
            ],
            Fields:
            [
                new FieldMetadata(idField, "Id", typeof(long), new ColumnReference(entityId, idColumn)),
                new FieldMetadata(nameField, "Name", typeof(string), new ColumnReference(entityId, nameColumn))
            ],
            PrimaryKey: new ColumnReference(entityId, idColumn)));

        var entity = new MutationEntitySchema(
            entityId,
            "Customer",
            new HashSet<ColumnId> { idColumn, nameColumn },
            new Dictionary<FieldId, ColumnId?>
            {
                [idField] = idColumn,
                [nameField] = nameColumn
            },
            idColumn);

        var operations = new[]
        {
            new MutationOperation(
                entity, MutationKind.Create,
                [new MutationFieldValue(idColumn, 101L), new MutationFieldValue(nameColumn, "Alice")],
                null, null, [idField, nameField]),
            new MutationOperation(
                entity, MutationKind.Create,
                [new MutationFieldValue(idColumn, 102L), new MutationFieldValue(nameColumn, "Bob")],
                null, null, [idField, nameField])
        };

        var plan = new MutationBatchPlan(operations, []);
        new PostgresBatchedMutationCompiler(metadata).Compile(plan);

        // The fake PostgreSQL command deliberately returns ordinal 2 before ordinal 1.
        // The provider must map by __fg_corr, never by reader position.
        var connection = new ReorderedPostgresConnection([
            (0, "{\"__fg_corr\":2,\"r_1\":102,\"r_2\":\"Bob\"}"),
            (0, "{\"__fg_corr\":1,\"r_1\":101,\"r_2\":\"Alice\"}")
        ]);

        var provider = new PostgresBatchedMutationExecutionProvider(connection, metadata);
        var result = provider.ExecuteBatch(plan, new ExecutionContext());

        Assert.Equal(101L, result.Results[0].ReturnedValues![idField]);
        Assert.Equal("Alice", result.Results[0].ReturnedValues![nameField]);
        Assert.Equal(102L, result.Results[1].ReturnedValues![idField]);
        Assert.Equal("Bob", result.Results[1].ReturnedValues![nameField]);
        Assert.Contains("__fg_corr", connection.LastCommandText, StringComparison.Ordinal);
        Assert.Contains("RETURNING", connection.LastCommandText, StringComparison.Ordinal);
    }


    [PostgreSqlFact]
    public async Task RealPostgresPropagatesGeneratedIdentityAcrossBatchedDependencyLevels()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING");

        var customer = new EntityId(11);
        var account = new EntityId(12);
        var metadata = new MetadataRegistry();

        metadata.Register(new EntityMetadata(
            customer,
            "Customer",
            [new ColumnMetadata(new ColumnId(1), "Id"), new ColumnMetadata(new ColumnId(2), "Name")],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(customer, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(customer, new ColumnId(1))));

        metadata.Register(new EntityMetadata(
            account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Name")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(long), new ColumnReference(account, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "CustomerId", typeof(long),
                    new ColumnReference(account, new ColumnId(2))),
                new FieldMetadata(new FieldId(3), "Name", typeof(string), new ColumnReference(account, new ColumnId(3)))
            ],
            PrimaryKey: new ColumnReference(account, new ColumnId(1))));

        var batch = new MutationBatchIntent(
        [
            new MutationIntent(
                customer,
                MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Alice")],
                ReturnFields: [new FieldId(1), new FieldId(2)]),
            new MutationIntent(
                customer,
                MutationKind.Create,
                [new MutationFieldValue(new ColumnId(2), "Bob")],
                ReturnFields: [new FieldId(1), new FieldId(2)]),
            new MutationIntent(
                account,
                MutationKind.Create,
                [
                    MutationFieldValue.FromPrevious(new ColumnId(2), 0, new FieldId(1)),
                    new MutationFieldValue(new ColumnId(3), "Alice Primary")
                ],
                ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)]),
            new MutationIntent(
                account,
                MutationKind.Create,
                [
                    MutationFieldValue.FromPrevious(new ColumnId(2), 1, new FieldId(1)),
                    new MutationFieldValue(new ColumnId(3), "Bob Primary")
                ],
                ReturnFields: [new FieldId(1), new FieldId(2), new FieldId(3)])
        ]);

        var plan = new MutationPlanner(metadata).Plan(batch);
        var compiled = new PostgresBatchedMutationCompiler(metadata).Compile(plan);

        Assert.Equal(2, plan.Dependencies.Count);
        Assert.Contains("g1_resolved", compiled.CommandText, StringComparison.Ordinal);
        Assert.Contains("JOIN g0_ordmap", compiled.CommandText, StringComparison.Ordinal);
        Assert.Contains("CustomerId__fg_corr", compiled.CommandText, StringComparison.Ordinal);

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var searchPath = new Npgsql.NpgsqlCommand(
                         "SET LOCAL search_path TO fg_correlation;", connection, transaction))
        {
            await searchPath.ExecuteNonQueryAsync();
        }

        // Reverse each physical result group. The dependency itself must still
        // have been resolved from the source group's explicit correlation key.
        await using var command = new Npgsql.NpgsqlCommand("", connection, transaction);
        command.CommandText = compiled.CommandText.Replace(
            "ORDER BY __grp, CASE WHEN __row ? '__fg_corr' THEN ((__row ->> '__fg_corr')::bigint) END",
            "ORDER BY __grp, CASE WHEN __row ? '__fg_corr' THEN ((__row ->> '__fg_corr')::bigint) END DESC",
            StringComparison.Ordinal);
        foreach (var binding in compiled.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + binding.Name;
            parameter.Value = binding.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var rows = new List<(int Group, int Corr, long Id, long? CustomerId, string Name)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var json = JsonDocument.Parse(reader.GetString(1)).RootElement;
                var group = reader.GetInt32(0);
                var corr = json.GetProperty("__fg_corr").GetInt32();
                var id = json.GetProperty("r_1").GetInt64();
                var name = group == 0
                    ? json.GetProperty("r_2").GetString()!
                    : json.GetProperty("r_3").GetString()!;
                long? customerId = group == 1
                    ? json.GetProperty("r_2").GetInt64()
                    : null;
                rows.Add((group, corr, id, customerId, name));
            }
        }

        var customers = rows.Where(x => x.Group == 0).ToDictionary(x => x.Corr);
        var accounts = rows.Where(x => x.Group == 1).ToDictionary(x => x.Corr);

        Assert.Equal(2, customers.Count);
        Assert.Equal(2, accounts.Count);
        Assert.NotEqual(customers[1].Id, customers[2].Id);
        Assert.Equal(customers[1].Id, accounts[1].CustomerId);
        Assert.Equal(customers[2].Id, accounts[2].CustomerId);

        await using var verify = new Npgsql.NpgsqlCommand("", connection, transaction);
        verify.CommandText =
            "SELECT c.\"Name\", a.\"Name\", a.\"CustomerId\" FROM \"Customer\" c JOIN \"Account\" a ON a.\"CustomerId\" = c.\"Id\" ORDER BY c.\"Name\";";
        await using var verifyReader = await verify.ExecuteReaderAsync();
        Assert.True(await verifyReader.ReadAsync());
        Assert.Equal("Alice", verifyReader.GetString(0));
        Assert.Equal("Alice Primary", verifyReader.GetString(1));
        Assert.True(await verifyReader.ReadAsync());
        Assert.Equal("Bob", verifyReader.GetString(0));
        Assert.Equal("Bob Primary", verifyReader.GetString(1));
        await transaction.RollbackAsync();
    }

    [PostgreSqlFact]
    public async Task RealPostgresPreservesCompilerOwnedCorrelationForGeneratedIdentityWhenRowsAreReversed()
    {
        var connectionString = Environment.GetEnvironmentVariable("FOUNDGINE_POSTGRES_CONNECTION_STRING");

        var entityId = new EntityId(1);
        var idColumn = new ColumnId(1);
        var nameColumn = new ColumnId(2);
        var idField = new FieldId(1);
        var nameField = new FieldId(2);

        var metadata = new MetadataRegistry();
        metadata.Register(new EntityMetadata(
            entityId,
            "Customer",
            [new ColumnMetadata(idColumn, "Id"), new ColumnMetadata(nameColumn, "Name")],
            Fields:
            [
                new FieldMetadata(idField, "Id", typeof(long), new ColumnReference(entityId, idColumn)),
                new FieldMetadata(nameField, "Name", typeof(string), new ColumnReference(entityId, nameColumn))
            ],
            PrimaryKey: new ColumnReference(entityId, idColumn)));

        var entity = new MutationEntitySchema(
            entityId,
            "Customer",
            new HashSet<ColumnId> { idColumn, nameColumn },
            new Dictionary<FieldId, ColumnId?> { [idField] = idColumn, [nameField] = nameColumn },
            idColumn);

        // Id is deliberately omitted from both operations. PostgreSQL owns the
        // generated identity. The user-visible payload is deliberately
        // identical, proving that neither a natural key nor result position can
        // be the correlation mechanism.
        var operations = new[]
        {
            new MutationOperation(entity, MutationKind.Create,
                [new MutationFieldValue(nameColumn, "same")],
                null, null, [idField, nameField]),
            new MutationOperation(entity, MutationKind.Create,
                [new MutationFieldValue(nameColumn, "same")],
                null, null, [idField, nameField])
        };

        var plan = new MutationBatchPlan(operations, []);
        var compiled = new PostgresBatchedMutationCompiler(metadata).Compile(plan);

        Assert.Contains("MERGE INTO \"Customer\" t", compiled.CommandText, StringComparison.Ordinal);
        Assert.Contains("RETURNING r.__fg_corr, t.\"Id\" AS \"r_1\"", compiled.CommandText, StringComparison.Ordinal);

        await using var connection = new Npgsql.NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var searchPath = new Npgsql.NpgsqlCommand(
                         "SET LOCAL search_path TO fg_correlation;", connection, transaction))
        {
            await searchPath.ExecuteNonQueryAsync();
        }

        // Execute the real compiler-generated top-level statement. Only the
        // terminal presentation order is changed. The data-modifying WITH
        // remains the top-level statement executed by PostgreSQL.
        const string ascendingOrder =
            "ORDER BY __grp, CASE WHEN __row ? '__fg_corr' THEN ((__row ->> '__fg_corr')::bigint) END";
        const string descendingOrder =
            "ORDER BY __grp, CASE WHEN __row ? '__fg_corr' THEN ((__row ->> '__fg_corr')::bigint) END DESC";
        Assert.Contains(ascendingOrder, compiled.CommandText, StringComparison.Ordinal);

        await using var command = new Npgsql.NpgsqlCommand("", connection, transaction);
        command.CommandText = compiled.CommandText.Replace(ascendingOrder, descendingOrder, StringComparison.Ordinal);
        foreach (var binding in compiled.Parameters)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = "@" + binding.Name;
            parameter.Value = binding.Value ?? DBNull.Value;
            command.Parameters.Add(parameter);
        }

        var rows = new List<(int Group, int Corr, long Id, string Name)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var json = JsonDocument.Parse(reader.GetString(1)).RootElement;
                rows.Add((
                    reader.GetInt32(0),
                    json.GetProperty("__fg_corr").GetInt32(),
                    json.GetProperty("r_1").GetInt64(),
                    json.GetProperty("r_2").GetString()!));
            }
        }

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].Corr);
        Assert.Equal(1, rows[1].Corr);
        Assert.Equal("same", rows[0].Name);
        Assert.Equal("same", rows[1].Name);
        Assert.NotEqual(rows[0].Id, rows[1].Id);

        // Re-map the physical result by compiler-owned correlation. The
        // generated identities must remain attached to their original logical
        // operations despite the reversed database result order.
        var byCorrelation = rows.ToDictionary(x => x.Corr);
        Assert.Equal(1, byCorrelation[1].Corr);
        Assert.Equal(2, byCorrelation[2].Corr);
        Assert.NotEqual(byCorrelation[1].Id, byCorrelation[2].Id);

        await using var verify = new Npgsql.NpgsqlCommand("", connection, transaction);
        verify.CommandText = "SELECT COUNT(*), COUNT(DISTINCT \"Id\") FROM \"Customer\";";
        await using var verifyReader = await verify.ExecuteReaderAsync();
        Assert.True(await verifyReader.ReadAsync());
        Assert.Equal(2, verifyReader.GetInt64(0));
        Assert.Equal(2, verifyReader.GetInt64(1));
    }

    private sealed class ReorderedPostgresConnection(
        IReadOnlyList<(int Group, string Json)> rows) : DbConnection
    {
        private ConnectionState _state = ConnectionState.Closed;

        public string LastCommandText { get; private set; } = string.Empty;
        public override string ConnectionString { get; set; } = string.Empty;
        public override string Database => "test";
        public override string DataSource => "fake-postgres";
        public override string ServerVersion => "17";
        public override ConnectionState State => _state;

        public override void Open() => _state = ConnectionState.Open;
        public override void Close() => _state = ConnectionState.Closed;

        public override void ChangeDatabase(string databaseName)
        {
        }

        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) =>
            throw new NotSupportedException();

        protected override DbCommand CreateDbCommand() => new ReorderedPostgresCommand(this, rows);

        private sealed class ReorderedPostgresCommand(
            ReorderedPostgresConnection owner,
            IReadOnlyList<(int Group, string Json)> rows) : DbCommand
        {
            private readonly FakeParameterCollection _parameters = new();

            public override string CommandText { get; set; } = string.Empty;
            public override int CommandTimeout { get; set; }
            public override CommandType CommandType { get; set; } = CommandType.Text;
            public override UpdateRowSource UpdatedRowSource { get; set; }
            protected override DbConnection? DbConnection { get; set; }
            protected override DbTransaction? DbTransaction { get; set; }
            public override bool DesignTimeVisible { get; set; }
            protected override DbParameterCollection DbParameterCollection => _parameters;

            public override void Prepare()
            {
            }

            public override void Cancel()
            {
            }

            protected override DbParameter CreateDbParameter() => new FakeParameter();

            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                owner.LastCommandText = CommandText;
                var table = new DataTable();
                table.Columns.Add("__grp", typeof(int));
                table.Columns.Add("__row", typeof(string));
                foreach (var row in rows)
                    table.Rows.Add(row.Group, row.Json);
                return table.CreateDataReader();
            }

            public override int ExecuteNonQuery() => throw new NotSupportedException();
            public override object? ExecuteScalar() => throw new NotSupportedException();
        }

        private sealed class FakeParameterCollection : DbParameterCollection
        {
            private readonly List<DbParameter> _items = [];
            public override int Count => _items.Count;
            public override object SyncRoot => ((ICollection)_items).SyncRoot!;

            public override int Add(object value)
            {
                _items.Add((DbParameter)value);
                return _items.Count - 1;
            }

            public override void AddRange(Array values)
            {
                foreach (var value in values) Add(value!);
            }

            public override void Clear() => _items.Clear();
            public override bool Contains(object value) => _items.Contains((DbParameter)value);
            public override bool Contains(string value) => _items.Any(x => x.ParameterName == value);
            public override void CopyTo(Array array, int index) => ((ICollection)_items).CopyTo(array, index);
            public override IEnumerator GetEnumerator() => _items.GetEnumerator();
            public override int IndexOf(object value) => _items.IndexOf((DbParameter)value);

            public override int IndexOf(string parameterName) =>
                _items.FindIndex(x => x.ParameterName == parameterName);

            public override void Insert(int index, object value) => _items.Insert(index, (DbParameter)value);
            public override void Remove(object value) => _items.Remove((DbParameter)value);
            public override void RemoveAt(int index) => _items.RemoveAt(index);
            public override void RemoveAt(string parameterName) => RemoveAt(IndexOf(parameterName));
            protected override DbParameter GetParameter(int index) => _items[index];
            protected override DbParameter GetParameter(string parameterName) => _items[IndexOf(parameterName)];
            protected override void SetParameter(int index, DbParameter value) => _items[index] = value;

            protected override void SetParameter(string parameterName, DbParameter value) =>
                _items[IndexOf(parameterName)] = value;
        }

        private sealed class FakeParameter : DbParameter
        {
            public override DbType DbType { get; set; }
            public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;
            public override bool IsNullable { get; set; }
            public override string ParameterName { get; set; } = string.Empty;
            public override string SourceColumn { get; set; } = string.Empty;
            public override object? Value { get; set; }
            public override bool SourceColumnNullMapping { get; set; }
            public override int Size { get; set; }
            public override byte Precision { get; set; }
            public override byte Scale { get; set; }

            public override void ResetDbType()
            {
            }
        }
    }
}

#pragma warning restore CS8765