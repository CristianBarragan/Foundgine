using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;
using Foundgine.Core.Semantic;

namespace Foundgine.E2E.Tests.Banking;

/// <summary>
/// Minimal port of the archived Banking semantic proof. Only the semantic
/// topology required by the new Foundgine structure is retained.
/// SQL joins, provider metadata and old planning types remain archived.
/// </summary>
public static class BankingSemanticModel
{
    public static readonly EntityId Customer = new(1);
    public static readonly EntityId Account = new(2);
    public static readonly EntityId Transaction = new(3);

    public static readonly RelationshipId CustomerAccounts = new(1);
    public static readonly RelationshipId AccountTransactions = new(2);
    public static readonly RelationshipId AccountCustomer = new(3);
    public static readonly RelationshipId TransactionAccount = new(4);

    public static SemanticModel Build() =>
        new SemanticModelBuilder()
            .Entity(Customer, "Customer", customer => customer
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(2), "Name", typeof(string))
                .Field(new FieldId(5), "TenantId", typeof(int))
                .Relationship(
                    CustomerAccounts,
                    "Accounts",
                    Account,
                    RelationshipCardinality.Many))
            .Entity(Account, "Account", account => account
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Balance", typeof(decimal))
                .Relationship(
                    AccountTransactions,
                    "Transactions",
                    Transaction,
                    RelationshipCardinality.Many)
                .Relationship(
                    AccountCustomer,
                    "Customer",
                    Customer,
                    RelationshipCardinality.One))
            .Entity(Transaction, "Transaction", transaction => transaction
                .Identity(new FieldId(1), "Id")
                .Field(new FieldId(3), "Amount", typeof(decimal))
                .Field(new FieldId(4), "TransactionDate", typeof(DateTime))
                .Relationship(
                    TransactionAccount,
                    "Account",
                    Account,
                    RelationshipCardinality.One))
            .Build();
}
