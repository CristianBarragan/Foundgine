#!/usr/bin/env python3
"""Generates an EF Core migration (Up/Down), its Designer and the model snapshot for PostgreSQL (Npgsql) from ONE
schema spec, so the three can never disagree. Used because `dotnet ef migrations add` needs a .NET SDK; with an SDK,
prefer `dotnet ef migrations add <Name>` and keep this only as a cross-check.

Usage from a spec file:  import efgen; g = efgen.Gen(...); g.ent(...); g.write(outdir)
"""
import os

T_INT, T_LONG, T_TS, T_BOOL, T_UUID, T_TEXT = "integer", "bigint", "timestamp with time zone", "boolean", "uuid", "text"


def vc(n): return f"character varying({n})"


class P:
    def __init__(s, name, clr, col, sql, nullable=False, maxlen=None, identity=False, never=False, default_sql=None, prec=None):
        s.name, s.clr, s.col, s.sql, s.nullable, s.maxlen, s.identity, s.never, s.default_sql, s.prec = \
            name, clr, col, sql, nullable, maxlen, identity, never, default_sql, prec


def S(name, col, n, nullable=False): return P(name, "string", col, vc(n), nullable, n)
def I(name, col, nullable=False): return P(name, "int", col, T_INT, nullable)
def D(name, col, nullable=False, default_sql=None): return P(name, "DateTime", col, T_TS, nullable, default_sql=default_sql)
def M(name, col, p, s): return P(name, "decimal", col, f"numeric({p},{s})", prec=(p, s))
def ID(name, col): return P(name, "int", col, T_INT, identity=True)


def q(s): return s.replace("\\", "\\\\").replace('"', '\\"')
def b(x): return "true" if x else "false"


class Gen:
    def __init__(self, ns, ctx, mig_id, mig_class, doc):
        self.ns, self.ctx, self.mig_id, self.mig_class, self.doc = ns, ctx, mig_id, mig_class, doc
        self.E = []

    def ent(self, clr, table, props, pk, pkname, idx=(), checks=(), fks=(), one_to_one=()):
        """clr: full CLR name. idx: (name, [props], unique, filter, descending). fks: (prop, principal clr, name)."""
        self.E.append(dict(clr=clr, cls=clr.split(".")[-1], table=table, props=props, pk=pk, pkname=pkname,
                           idx=list(idx), checks=list(checks), fks=list(fks), one=set(one_to_one)))

    def _e(self, clr): return next(e for e in self.E if e["clr"] == clr)
    def _col(self, e, prop): return next(p for p in e["props"] if p.name == prop).col

    def _column_line(self, p):
        args = [f'type: "{p.sql}"']
        if p.maxlen: args.append(f"maxLength: {p.maxlen}")
        if p.prec: args += [f"precision: {p.prec[0]}", f"scale: {p.prec[1]}"]
        args.append(f"nullable: {b(p.nullable)}")
        if p.default_sql: args.append(f'defaultValueSql: "{p.default_sql}"')
        clr = p.clr + ("?" if p.nullable and p.clr != "string" else "")
        line = f"{p.col} = table.Column<{clr}>({', '.join(args)})"
        if p.identity:
            line += '\n                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)'
        return line

    def migration(self):
        up = []
        for e in self.E:
            cols = ",\n                    ".join(self._column_line(p) for p in e["props"])
            pk = e["pk"]
            expr = f"x => x.{self._col(e, pk[0])}" if len(pk) == 1 else "x => new { " + ", ".join(f"x.{self._col(e, k)}" for k in pk) + " }"
            cons = [f'table.PrimaryKey("{e["pkname"]}", {expr});']
            for prop, principal, name in e["fks"]:
                pe = self._e(principal)
                cons.append(f'table.ForeignKey(name: "{name}", column: x => x.{self._col(e, prop)}, principalTable: "{pe["table"]}", principalColumn: "{self._col(pe, pe["pk"][0])}");')
            for name, sql in e["checks"]:
                cons.append(f'table.CheckConstraint("{name}", "{q(sql)}");')
            cons = "\n                    ".join(cons)
            up.append(f'''            migrationBuilder.CreateTable(
                name: "{e["table"]}",
                columns: table => new
                {{
                    {cols}
                }},
                constraints: table =>
                {{
                    {cons}
                }});
''')
            for name, props, unique, filt, desc in e["idx"]:
                cols_ = ", ".join(f'"{self._col(e, p)}"' for p in props)
                extra = (", unique: true" if unique else "") + (f', filter: "{q(filt)}"' if filt else "") + \
                        (", descending: new[] { " + ", ".join(b(d) for d in desc) + " }" if desc else "")
                colspec = f'column: "{self._col(e, props[0])}"' if len(props) == 1 else f"columns: new[] {{ {cols_} }}"
                up.append(f'''            migrationBuilder.CreateIndex(
                name: "{name}",
                table: "{e["table"]}",
                {colspec}{extra});
''')
        down = "\n".join(f'            migrationBuilder.DropTable(name: "{e["table"]}");' for e in reversed(self.E))
        return f'''using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace {self.ns};

/// <summary>{self.doc}</summary>
public partial class {self.mig_class} : Migration
{{
    protected override void Up(MigrationBuilder migrationBuilder)
    {{
{chr(10).join(up)}    }}

    protected override void Down(MigrationBuilder migrationBuilder)
    {{
{down}
    }}
}}
'''

    def _model_body(self, indent):
        L = []
        for e in self.E:
            L.append(f'modelBuilder.Entity("{e["clr"]}", b =>\n{{')
            for p in e["props"]:
                clr = p.clr + ("?" if p.nullable and p.clr != "string" else "")
                line = f'    b.Property<{clr}>("{p.name}")'
                if p.identity: line += ".ValueGeneratedOnAdd()"
                if p.never: line += ".ValueGeneratedNever()"
                if not p.nullable and p.clr == "string": line += ".IsRequired()"
                if p.maxlen: line += f".HasMaxLength({p.maxlen})"
                if p.prec: line += f".HasPrecision({p.prec[0]}, {p.prec[1]})"
                line += f'.HasColumnType("{p.sql}").HasColumnName("{p.col}")'
                if p.default_sql: line += f'.HasDefaultValueSql("{p.default_sql}")'
                L.append(line + ";")
                if p.identity:
                    L.append(f'    NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(b.Property<{p.clr}>("{p.name}"));')
            keys = ", ".join(f'"{k}"' for k in e["pk"])
            L.append(f'    b.HasKey({keys}).HasName("{e["pkname"]}");')
            for name, props, unique, filt, desc in e["idx"]:
                ps = ", ".join(f'"{p}"' for p in props)
                line = f'    b.HasIndex(new[] {{ {ps} }}, "{name}")'
                if unique: line += ".IsUnique()"
                if filt: line += f'.HasFilter("{q(filt)}")'
                if desc: line += ".IsDescending(" + ", ".join(b(d) for d in desc) + ")"
                L.append(line + ";")
            if e["checks"]:
                L.append(f'    b.ToTable("{e["table"]}", t =>\n    {{')
                for name, sql in e["checks"]: L.append(f'        t.HasCheckConstraint("{name}", "{q(sql)}");')
                L.append("    });")
            else:
                L.append(f'    b.ToTable("{e["table"]}");')
            L.append("});\n")
        for e in self.E:
            for prop, principal, name in e["fks"]:
                one = prop in e["one"]
                rel = "WithOne()" if one else "WithMany()"
                fk = f'.HasForeignKey("{e["clr"]}", "{prop}")' if one else f'.HasForeignKey("{prop}")'
                L.append(f'modelBuilder.Entity("{e["clr"]}", b =>\n{{\n    b.HasOne("{principal}", null).{rel}{fk}'
                         f'.HasConstraintName("{name}").OnDelete(DeleteBehavior.NoAction).IsRequired();\n}});\n')
        pad = " " * indent
        return "\n".join(pad + l if l else l for l in "\n".join(L).split("\n"))

    HEAD = '''            modelBuilder
                .HasAnnotation("ProductVersion", "10.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

'''
    USINGS = '''using System;
using Foundgine.Providers.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
'''

    def write(self, out):
        body = self.HEAD + self._model_body(12)
        designer = f'''{self.USINGS}
namespace {self.ns};

[DbContext(typeof({self.ctx}))]
[Migration("{self.mig_id}_{self.mig_class}")]
partial class {self.mig_class}
{{
    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {{
#pragma warning disable 612, 618
{body}
#pragma warning restore 612, 618
    }}
}}
'''
        snapshot = f'''{self.USINGS}
namespace {self.ns};

[DbContext(typeof({self.ctx}))]
partial class {self.ctx}ModelSnapshot : ModelSnapshot
{{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {{
#pragma warning disable 612, 618
{body}
#pragma warning restore 612, 618
    }}
}}
'''
        os.makedirs(out, exist_ok=True)
        stem = f"{self.mig_id}_{self.mig_class}"
        open(f"{out}/{stem}.cs", "w").write(self.migration())
        open(f"{out}/{stem}.Designer.cs", "w").write(designer)
        open(f"{out}/{self.ctx}ModelSnapshot.cs", "w").write(snapshot)
        print("generated", len(self.E), "tables ->", out)
