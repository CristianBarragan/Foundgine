# EF Core Relational runtime fix

The Hot Chocolate benchmark was failing during `BankingEntityContext.OnModelCreating` with:

`Could not load file or assembly 'Microsoft.EntityFrameworkCore.Relational, Version=9.0.4.0'`

The runtime project now references `Microsoft.EntityFrameworkCore.Relational` 9.0.4 explicitly, and the shared `CoffeeBeanery.Database` project does too. This is intentional: `Microsoft.EntityFrameworkCore.Design` is a design-time package and is not a substitute for the runtime relational assembly.

All EF Core runtime packages in the active CoffeeBeanery benchmark path remain on 9.0.4, matching `Npgsql.EntityFrameworkCore.PostgreSQL` 9.0.4.

The fix is limited to dependency resolution; it does not change the relationship mappings or benchmark behavior.
