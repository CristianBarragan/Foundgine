package com.foundgine.providers.aot.generator;

import static org.junit.jupiter.api.Assertions.*;

import org.junit.jupiter.api.Test;

import java.nio.file.*;

import javax.tools.JavaCompiler;
import javax.tools.ToolProvider;

class FoundgineAotProcessorParityTest {
    @Test
    void generatesDeterministicMetadataWithoutReflection() throws Exception {
        JavaCompiler compiler = ToolProvider.getSystemJavaCompiler();
        assertNotNull(compiler, "JDK compiler is required for AOT generator tests");

        Path root = Files.createTempDirectory("foundgine-aot-test");
        Path source = root.resolve("Fixture.java");
        Files.writeString(
                source,
                """
                package fixture;
                import com.foundgine.providers.aot.*;
                @FoundgineEntity(name="Customer")
                @FoundgineOrmEntity
                @FoundgineAlias(value="Client", weight=95)
                public class Fixture {
                    @FoundgineField(id=11, isPrimaryKey=true)
                    int id;
                    @FoundgineSemanticDimension("tenant")
                    String tenantId;
                    String name;
                    public int getId() { return id; }
                    public void setId(int id) { this.id = id; }
                    public String getName() { return name; }
                    public void setName(String name) { this.name = name; }
                    @FoundgineRelationship(target=Account.class, foreignKey="customerId", principalKey="id", id=77, name="Accounts")
                    Account accounts;
                    @FoundgineEntity(name="Account")
                    @FoundgineOrmEntity
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
        int result =
                compiler.run(
                        null,
                        null,
                        null,
                        "-cp",
                        cp,
                        "-processorpath",
                        cp,
                        "-processor",
                        FoundgineAotProcessor.class.getName(),
                        "-Afoundgine.generated.package=fixture.generated",
                        "-Afoundgine.generated.name=GeneratedFoundgineMetadata",
                        "-s",
                        generated.toString(),
                        "-d",
                        classes.toString(),
                        source.toString());
        assertEquals(0, result);

        Path generatedFile =
                generated.resolve(
                        Path.of("fixture", "generated", "GeneratedFoundgineMetadata.java"));

        assertTrue(
                Files.exists(generatedFile),
                "Generated metadata source was not created: "
                        + generatedFile
                        + "\nGenerated directory contents:\n"
                        + Files.walk(generated)
                                .map(Path::toString)
                                .sorted()
                                .collect(java.util.stream.Collectors.joining("\n")));
        String text = Files.readString(generatedFile);
        assertTrue(text.contains("new EntityId("));
        assertTrue(text.contains("new FieldId(11L)"));
        assertTrue(text.contains("new FieldId(88L)"));
        assertTrue(text.contains("new AliasDeclaration(\"Client\", 95)"));
        assertTrue(text.contains("\"tenant\""));
        assertTrue(text.contains("new RelationshipId(77L)"));
        assertFalse(text.contains("FoundgineMetadataGenerator"));
        Path jpaModels = generated.resolve(Path.of("fixture", "generated", "GeneratedJpaModels.java"));
        assertTrue(Files.exists(jpaModels), "Generated direct-access JPA model source was not created");
        String jpaText = Files.readString(jpaModels);
        assertTrue(jpaText.contains("java.util.Set.of(") && jpaText.contains("\"id\")"),
                "generated model must carry its attribute set and identifier attribute");
        Path ormMaps = generated.resolve(Path.of("fixture", "generated", "GeneratedOrmMaps.java"));
        assertTrue(Files.exists(ormMaps), "Generated ORM entity map source was not created");
        String mapsText = Files.readString(ormMaps);
        assertTrue(mapsText.contains("Map.entry(\"Customer\", fixture.Fixture.class)"),
                "an @FoundgineEntity name backs the ORM entity when @FoundgineOrmEntity names none");
        assertTrue(jpaText.contains("new JpaEntityModel<>(fixture.Fixture.class"));
        assertTrue(jpaText.contains("new JpaEntityModel<>(fixture.Fixture.Account.class"));
        assertFalse(jpaText.contains("java.lang.reflect"));
        assertFalse(jpaText.contains("setAccessible"));
        assertTrue(jpaText.contains("e.getId()"));
        assertTrue(jpaText.contains("e.setId((int) v)"));
    }
}
