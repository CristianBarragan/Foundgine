# EF Core comparator (UNBUILT)

Hand-written EF Core 10 equivalents of the 11 `Scenarios/`. **Never compiled**: the authoring sandbox cannot restore NuGet packages.
Only a Roslyn *syntax* check was run, so EF API usage (e.g. `UsingEntity`, `OwnsOne(...).ToTable`, `UseTptMappingStrategy`) is unverified
against the real library and may need fixes.

Hot Chocolate has no separate column: its data layer is EF Core, and GraphQL is outside the mapping benchmark, so Hot Chocolate + EF Core
adds no mapping code beyond what is here.

Each file has three counted sections: `ENTITIES` (persistence classes EF requires), `CONFIG` (model configuration + the tenant/owner
`HasQueryFilter`), `MAPPING` (entity -> domain projection). Shared plumbing is in `Common.cs` (not counted). The comparator was written by the
same author as the Foundgine side; to run it, add a project that references `Microsoft.EntityFrameworkCore` 10.x and these files plus `../Domain.cs`.
