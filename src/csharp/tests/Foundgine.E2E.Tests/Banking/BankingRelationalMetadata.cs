using Foundgine.Core.Semantic.Metadata;
using Foundgine.Core.Abstractions;

namespace Foundgine.E2E.Tests.Banking;

/// <summary>
/// The smallest relational metadata required by the SQL proof. It maps the semantic
/// Banking proof to SQLite storage without introducing storage concepts into
/// Foundgine.Core.Semantic.
/// </summary>
public static class BankingRelationalMetadata
{
    public static MetadataRegistry Build()
    {
        var customer = new EntityMetadata(
            BankingSemanticModel.Customer,
            "Customer",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "Name"),
                new ColumnMetadata(new ColumnId(5), "TenantId")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int),
                    new ColumnReference(BankingSemanticModel.Customer, new ColumnId(1))),
                new FieldMetadata(new FieldId(2), "Name", typeof(string),
                    new ColumnReference(BankingSemanticModel.Customer, new ColumnId(2))),
                new FieldMetadata(new FieldId(5), "TenantId", typeof(int),
                    new ColumnReference(BankingSemanticModel.Customer, new ColumnId(5)))
            ],
            PrimaryKey: new ColumnReference(BankingSemanticModel.Customer, new ColumnId(1)));

        var account = new EntityMetadata(
            BankingSemanticModel.Account,
            "Account",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "CustomerId"),
                new ColumnMetadata(new ColumnId(3), "Balance")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int),
                    new ColumnReference(BankingSemanticModel.Account, new ColumnId(1))),
                new FieldMetadata(new FieldId(3), "Balance", typeof(decimal),
                    new ColumnReference(BankingSemanticModel.Account, new ColumnId(3))),
                // Internal foreign-key mapping only: CustomerId has no corresponding
                // field in the semantic model, so it is never selectable or
                // discoverable through a capability contract. It exists solely so
                // relational/in-memory providers can resolve the CustomerAccounts
                // and AccountCustomer relationship key columns during traversal.
                new FieldMetadata(new FieldId(4), "CustomerId", typeof(int),
                    new ColumnReference(BankingSemanticModel.Account, new ColumnId(2)))
            ],
            PrimaryKey: new ColumnReference(BankingSemanticModel.Account, new ColumnId(1)));

        var transaction = new EntityMetadata(
            BankingSemanticModel.Transaction,
            "Transaction",
            [
                new ColumnMetadata(new ColumnId(1), "Id"),
                new ColumnMetadata(new ColumnId(2), "AccountId"),
                new ColumnMetadata(new ColumnId(3), "Amount"),
                new ColumnMetadata(new ColumnId(4), "TransactionDate")
            ],
            Fields:
            [
                new FieldMetadata(new FieldId(1), "Id", typeof(int),
                    new ColumnReference(BankingSemanticModel.Transaction, new ColumnId(1))),
                new FieldMetadata(new FieldId(3), "Amount", typeof(decimal),
                    new ColumnReference(BankingSemanticModel.Transaction, new ColumnId(3))),
                new FieldMetadata(new FieldId(4), "TransactionDate", typeof(DateTime),
                    new ColumnReference(BankingSemanticModel.Transaction, new ColumnId(4)))
            ],
            PrimaryKey: new ColumnReference(BankingSemanticModel.Transaction, new ColumnId(1)));

        var customerAccounts = new RelationshipMetadata(
            BankingSemanticModel.CustomerAccounts,
            BankingSemanticModel.Customer,
            BankingSemanticModel.Account,
            "Accounts",
            new ColumnReference(BankingSemanticModel.Customer, new ColumnId(1)),
            new ColumnReference(BankingSemanticModel.Account, new ColumnId(2)));

        var accountTransactions = new RelationshipMetadata(
            BankingSemanticModel.AccountTransactions,
            BankingSemanticModel.Account,
            BankingSemanticModel.Transaction,
            "Transactions",
            new ColumnReference(BankingSemanticModel.Account, new ColumnId(1)),
            new ColumnReference(BankingSemanticModel.Transaction, new ColumnId(2)));

        var accountCustomer = new RelationshipMetadata(
            BankingSemanticModel.AccountCustomer,
            BankingSemanticModel.Account,
            BankingSemanticModel.Customer,
            "Customer",
            new ColumnReference(BankingSemanticModel.Account, new ColumnId(2)),
            new ColumnReference(BankingSemanticModel.Customer, new ColumnId(1)));

        var transactionAccount = new RelationshipMetadata(
            BankingSemanticModel.TransactionAccount,
            BankingSemanticModel.Transaction,
            BankingSemanticModel.Account,
            "Account",
            new ColumnReference(BankingSemanticModel.Transaction, new ColumnId(2)),
            new ColumnReference(BankingSemanticModel.Account, new ColumnId(1)));

        var registry = new MetadataRegistry();
        registry.Register(customer);
        registry.Register(account);
        registry.Register(transaction);
        registry.Register(customerAccounts);
        registry.Register(accountTransactions);
        registry.Register(accountCustomer);
        registry.Register(transactionAccount);
        return registry;
    }
}