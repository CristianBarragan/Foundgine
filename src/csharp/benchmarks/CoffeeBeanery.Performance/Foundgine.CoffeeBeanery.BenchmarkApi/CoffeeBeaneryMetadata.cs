using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic.Metadata;

namespace Foundgine.CoffeeBeanery.BenchmarkApi;

internal static class CoffeeBeaneryMetadata
{
    internal static MetadataRegistry Build()
    {
        var registry = new MetadataRegistry();

        var customer = CoffeeBeanerySemanticModel.Customer;
        var relationship = CoffeeBeanerySemanticModel.CustomerBankingRelationship;
        var contract = CoffeeBeanerySemanticModel.Contract;
        var transaction = CoffeeBeanerySemanticModel.Transaction;

        registry.Register(Entity(
            customer,
            "Customer",
            "Banking",
            [
                (1, "Id"), (2, "CustomerKey"), (3, "FirstName"),
                (4, "LastName"), (5, "FullName")
            ],
            [
                (1, "Id", typeof(int)), (2, "CustomerKey", typeof(Guid)),
                (3, "FirstName", typeof(string)), (4, "LastName", typeof(string)),
                (5, "FullName", typeof(string))
            ]));

        registry.Register(Entity(
            relationship,
            "CustomerBankingRelationship",
            "Banking",
            [(1, "Id"), (2, "CustomerBankingRelationshipKey"), (3, "CustomerId")],
            [
                (1, "Id", typeof(int)), (2, "CustomerBankingRelationshipKey", typeof(Guid)),
                (3, "CustomerId", typeof(int?))
            ]));

        registry.Register(Entity(
            contract,
            "Contract",
            "Lending",
            [(1, "Id"), (2, "ContractKey"), (3, "ContractType"), (4, "Amount"), (5, "CustomerBankingRelationshipId")],
            [
                (1, "Id", typeof(int)), (2, "ContractKey", typeof(Guid)),
                (3, "ContractType", typeof(int?)), (4, "Amount", typeof(decimal?)),
                (5, "CustomerBankingRelationshipId", typeof(int?))
            ]));

        registry.Register(Entity(
            transaction,
            "Transaction",
            "Lending",
            [(1, "Id"), (2, "TransactionKey"), (3, "Amount"), (4, "Balance"), (5, "ContractId")],
            [
                (1, "Id", typeof(int)), (2, "TransactionKey", typeof(Guid)),
                (3, "Amount", typeof(decimal?)), (4, "Balance", typeof(decimal?)),
                (5, "ContractId", typeof(int?))
            ]));

        registry.Register(Relationship(
            CoffeeBeanerySemanticModel.CustomerRelationships,
            customer, relationship, "CustomerBankingRelationship", 1, 3));
        registry.Register(Relationship(
            CoffeeBeanerySemanticModel.RelationshipContracts,
            relationship, contract, "Contract", 1, 4));
        registry.Register(Relationship(
            CoffeeBeanerySemanticModel.ContractTransactions,
            contract, transaction, "Transaction", 1, 5));

        return registry;
    }

    private static EntityMetadata Entity(
        EntityId id,
        string name,
        string schema,
        (int Id, string Name)[] columns,
        (int Id, string Name, Type Type)[] fields)
    {
        var columnMetadata = columns
            .Select(x => new ColumnMetadata(new ColumnId(ushort.Parse(x.Id.ToString())), x.Name))
            .ToArray();

        var fieldMetadata = fields
            .Select(x => new FieldMetadata(
                new FieldId(ushort.Parse(x.Id.ToString())),
                x.Name,
                x.Type,
                new ColumnReference(id, new ColumnId(ushort.Parse(x.Id.ToString())))))
            .ToArray();

        return new EntityMetadata(
            id,
            name,
            columnMetadata,
            $"{schema}.{name}",
            fieldMetadata,
            new ColumnReference(id, new ColumnId(1)));
    }

    private static RelationshipMetadata Relationship(
        RelationshipId id,
        EntityId source,
        EntityId target,
        string name,
        int sourceKey,
        int targetKey) =>
        new(
            id,
            source,
            target,
            name,
            new ColumnReference(source, new ColumnId(ushort.Parse(sourceKey.ToString()))),
            new ColumnReference(target, new ColumnId(ushort.Parse(targetKey.ToString()))));
}