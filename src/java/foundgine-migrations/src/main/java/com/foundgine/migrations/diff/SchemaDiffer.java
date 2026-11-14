package com.foundgine.migrations.diff;

import com.foundgine.migrations.diff.Operation.*;
import com.foundgine.migrations.schema.*;
import java.util.ArrayList;
import java.util.List;

/** Compares snapshots and orders operations so referenced tables exist before foreign keys are added. */
public final class SchemaDiffer {
    private SchemaDiffer() {}
    public static List<Operation> diff(DatabaseSchema from, DatabaseSchema to) {
        List<Operation> extensions = new ArrayList<>(), creates = new ArrayList<>(), drops = new ArrayList<>(), changes = new ArrayList<>(), indexes = new ArrayList<>(), constraints = new ArrayList<>(), columnDrops = new ArrayList<>(), tableDrops = new ArrayList<>();
        for (PostgresExtensionSchema e : to.extensions()) if (from.extension(e.name()).isEmpty()) extensions.add(new CreateExtension(e));
        for (TableSchema target : to.tables()) {
            TableSchema current = from.table(target.name()).orElse(null);
            if (current == null) {
                creates.add(new CreateTable(target));
                for (ForeignKeySchema fk : target.foreignKeys()) constraints.add(new AddForeignKey(target.name(), fk));
                continue;
            }
            if (!current.primaryKey().equals(target.primaryKey())) throw new UnsupportedOperationException("The primary key of " + target.name() + " changed from " + current.primaryKey() + " to " + target.primaryKey() + ". Changing a key cannot be generated safely; write that migration by hand.");
            for (ColumnSchema column : target.columns()) {
                ColumnSchema old = current.column(column.name()).orElse(null);
                if (old == null) { changes.add(new AddColumn(target.name(), column)); continue; }
                if (!old.normalizedType().equals(column.normalizedType())) changes.add(new AlterColumnType(target.name(), column.name(), old.type(), column.type()));
                if (old.nullable() != column.nullable()) changes.add(new AlterColumnNullability(target.name(), column.name(), column.nullable()));
                if (!java.util.Objects.equals(old.defaultExpression(), column.defaultExpression())) changes.add(new SetColumnDefault(target.name(), column.name(), old.defaultExpression(), column.defaultExpression()));
                if (!java.util.Objects.equals(old.identity(), column.identity())) throw new UnsupportedOperationException("Changing identity generation for " + target.name() + "." + column.name() + " requires a hand-written migration.");
            }
            for (ColumnSchema old : current.columns()) if (target.column(old.name()).isEmpty()) columnDrops.add(new DropColumn(target.name(), old));
            for (IndexSchema ix : target.indexes()) { IndexSchema old = current.index(ix.name()).orElse(null); if (old == null) indexes.add(new CreateIndex(target.name(), ix)); else if (!old.equals(ix)) { indexes.add(new DropIndex(target.name(), old)); indexes.add(new CreateIndex(target.name(), ix)); } }
            for (IndexSchema old : current.indexes()) if (target.index(old.name()).isEmpty()) indexes.add(new DropIndex(target.name(), old));
            for (ForeignKeySchema fk : current.foreignKeys()) if (target.foreignKey(fk.name()).isEmpty() || !target.foreignKey(fk.name()).orElseThrow().equals(fk)) constraints.add(new DropForeignKey(target.name(), fk));
            for (ForeignKeySchema fk : target.foreignKeys()) if (current.foreignKey(fk.name()).isEmpty() || !current.foreignKey(fk.name()).orElseThrow().equals(fk)) constraints.add(new AddForeignKey(target.name(), fk));
            for (CheckConstraintSchema ck : current.checks()) if (target.check(ck.name()).isEmpty() || !target.check(ck.name()).orElseThrow().equals(ck)) constraints.add(new DropCheck(target.name(), ck));
            for (CheckConstraintSchema ck : target.checks()) if (current.check(ck.name()).isEmpty() || !current.check(ck.name()).orElseThrow().equals(ck)) constraints.add(new AddCheck(target.name(), ck));
        }
        for (TableSchema old : from.tables()) if (to.table(old.name()).isEmpty()) tableDrops.add(new DropTable(old));
        List<Operation> result = new ArrayList<>();
        result.addAll(extensions); result.addAll(creates); result.addAll(indexes); result.addAll(changes); result.addAll(constraints); result.addAll(columnDrops); result.addAll(tableDrops);
        for (PostgresExtensionSchema e : from.extensions()) if (to.extension(e.name()).isEmpty()) drops.add(new DropExtension(e));
        result.addAll(drops);
        return result;
    }
}
