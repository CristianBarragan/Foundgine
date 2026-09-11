package com.foundgine.providers.aot.generator;

import org.junit.jupiter.api.Test;

import javax.tools.JavaCompiler;
import javax.tools.ToolProvider;
import java.nio.file.*;

import static org.junit.jupiter.api.Assertions.*;

class FoundgineAotProcessorParityTest {
    @Test
    void generatesDeterministicMetadataWithoutReflection() throws Exception {
        JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
        assertNotNull(compiler, "JDK compiler is required for AOT generator tests");

        Path root = Files.createTempDirectory("foundgine-aot-test");
        Path source = root.resolve("Fixture.java");
        Files.writeString(source, """
                package fixture;
                import com.foundgine.providers.aot.*;
                @FoundgineEntity(name="Customer")
                @FoundgineAlias(value="Client", weight=95)
                public class Fixture {
                    @FoundgineField(id=11, isPrimaryKey=true)
                    int id;
                    @FoundgineSemanticDimension("tenant")
                    String tenantId;
                    String name;
                    @FoundgineRelationship(target=Account.class, foreignKey="customerId", principalKey="id", id=77, name="Accounts")
                    Account accounts;
                    @FoundgineEntity(name="Account")
                    public static class Account {
                        @FoundgineField(id=88, isPrimaryKey=true)
                        int id;
                        int customerId;
                        String name;
                    }
                }
                """);
        Path generated = root.resolve("generated");
        Path classes = root.resolve("classes");
        Files.createDirectories(generated);
        Files.createDirectories(classes);

        String cp = System.getProperty("java.class.path");
        int result = compiler.run(null, null, null,
                "-cp", cp,
                "-processorpath", cp,
                "-processor", FoundgineAotProcessor.class.getName(),
                "-Afoundgine.generated.package=fixture.generated",
                "-Afoundgine.generated.name=GeneratedFoundgineMetadata",
                "-s", generated.toString(),
                "-d", classes.toString(),
                source.toString());
        assertEquals(0, result);

        Path generatedFile = generated.resolve("fixture/generated/GeneratedFoundgineMetadata.java");
        assertTrue(Files.exists(generatedFile));
        String text = Files.readString(generatedFile);
        assertTrue(text.contains("new EntityId("));
        assertTrue(text.contains("new FieldId(11L)"));
        assertTrue(text.contains("new FieldId(88L)"));
        assertTrue(text.contains("new AliasDeclaration(\"Client\", 95)"));
        assertTrue(text.contains("\"tenant\""));
        assertTrue(text.contains("new RelationshipId(77L)"));
        assertFalse(text.contains("FoundgineMetadataGenerator"));
    }
}
