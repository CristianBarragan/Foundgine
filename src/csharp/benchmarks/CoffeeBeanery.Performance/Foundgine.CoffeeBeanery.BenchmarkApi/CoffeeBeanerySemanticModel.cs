using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;

namespace Foundgine.CoffeeBeanery.BenchmarkApi;

internal static class CoffeeBeanerySemanticModel
{
    internal static readonly EntityId Customer = new(1);
    internal static readonly EntityId CustomerBankingRelationship = new(2);
    internal static readonly EntityId Contract = new(3);
    internal static readonly EntityId Transaction = new(4);

    internal static readonly RelationshipId CustomerRelationships = new(1);
    internal static readonly RelationshipId RelationshipContracts = new(2);
    internal static readonly RelationshipId ContractTransactions = new(3);

    internal static SemanticModel Build() =>
        new SemanticModelBuilder()
            .Entity(Customer, "Customer", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerKey", typeof(Guid))
                .Field(new FieldId(3), "FirstName", typeof(string))
                .Field(new FieldId(4), "LastName", typeof(string))
                .Field(new FieldId(5), "FullName", typeof(string))
                .Relationship(
                    CustomerRelationships,
                    "CustomerBankingRelationship",
                    CustomerBankingRelationship,
                    RelationshipCardinality.Many))
            .Entity(CustomerBankingRelationship, "CustomerBankingRelationship", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "CustomerBankingRelationshipKey", typeof(Guid))
                .Field(new FieldId(3), "CustomerId", typeof(int?))
                .Relationship(
                    RelationshipContracts,
                    "Contract",
                    Contract,
                    RelationshipCardinality.Many))
            .Entity(Contract, "Contract", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "ContractKey", typeof(Guid))
                .Field(new FieldId(3), "ContractType", typeof(int?))
                .Field(new FieldId(4), "Amount", typeof(decimal?))
                .Field(new FieldId(5), "CustomerBankingRelationshipId", typeof(int?))
                .Relationship(
                    ContractTransactions,
                    "Transaction",
                    Transaction,
                    RelationshipCardinality.Many))
            .Entity(Transaction, "Transaction", e => e
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "TransactionKey", typeof(Guid))
                .Field(new FieldId(3), "Amount", typeof(decimal?))
                .Field(new FieldId(4), "Balance", typeof(decimal?))
                .Field(new FieldId(5), "ContractId", typeof(int?)))
            .Build();
}
