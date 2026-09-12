package com.foundgine.core.semantic.planning;

import org.junit.jupiter.api.Test;
import org.w3c.dom.Document;
import org.w3c.dom.Element;
import org.w3c.dom.NodeList;

import javax.xml.parsers.DocumentBuilderFactory;
import java.io.File;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.stream.Stream;

import static org.junit.jupiter.api.Assertions.assertTrue;

/**
 * Port of
 * {@code Foundgine.Core.Semantic.Planning.Tests.ArchitectureBoundaryTests}.
 *
 * <p>
 * <b>Porting decisions:</b>
 * <ul>
 * <li>The C# test locates the repo root by walking up from the test assembly's
 * {@code AppContext.BaseDirectory} looking for {@code Foundgine.sln}.
 * Maven/Surefire runs tests with the module directory (e.g.
 * {@code src/java/foundgine-core}) as the working directory, so
 * {@link #findRepositoryRoot()} walks up from there instead, using the same
 * {@code Foundgine.sln} marker (it lives at the single repo root shared by both
 * the C# and Java trees).</li>
 * <li>C# {@code .csproj}'s {@code ProjectReference}/{@code PackageReference}
 * elements become Maven's {@code <dependency>} elements (both intra-repo module
 * dependencies and external packages are declared the same way in Maven, unlike
 * the C# split).</li>
 * <li>The C# scan for the historical engine's former project name walks the
 * whole {@code src/} tree (both language ports). This scans only
 * {@code src/java} for {@code *.java} and {@code pom.xml} files, since the C#
 * parity test already covers the {@code src/csharp} tree and duplicating that
 * scan here would just re-check the same files twice. This file is itself
 * excluded from the scan below, since discussing the boundary check necessarily
 * requires naming what it forbids.</li>
 * </ul>
 */
class ArchitectureBoundaryParityTest {

	@Test
	void coreModulesDoNotReferenceTransportOrProviderPackages() {
		var root = findRepositoryRoot();

		assertModuleDependenciesDoNotContain(root, "src/java/foundgine-core/pom.xml", "graphql", "hotchocolate", "sql",
				"npgsql", "inmemory", "intent-json", "intentjson", "aot");

		assertModuleDependenciesDoNotContain(root, "src/java/foundgine-runtime/pom.xml", "graphql", "hotchocolate",
				"sql", "npgsql", "inmemory");
	}

	@Test
	void activeSourceContainsNoGraphgineReferences() throws IOException {
		var root = findRepositoryRoot();
		var source = root.resolve("src").resolve("java");

		var selfPath = Path.of("src/test/java/com/foundgine/core/semantic/planning/ArchitectureBoundaryParityTest.java")
				.toAbsolutePath().normalize();

		List<String> offenders = new ArrayList<>();
		try (Stream<Path> paths = Files.walk(source)) {
			for (var path : paths.filter(Files::isRegularFile).toList()) {
				var name = path.getFileName().toString().toLowerCase(Locale.ROOT);
				if (!name.endsWith(".java") && !name.equals("pom.xml")) {
					continue;
				}
				if (path.toAbsolutePath().normalize().equals(selfPath)) {
					continue;
				}
				var content = Files.readString(path, StandardCharsets.UTF_8);
				if (content.toLowerCase(Locale.ROOT).contains("graphgine")) {
					offenders.add(root.relativize(path).toString());
				}
			}
		}

		assertTrue(offenders.isEmpty(), "Active src/java must not depend on historical Graphgine material. Offenders: "
				+ String.join(", ", offenders));
	}

	private static void assertModuleDependenciesDoNotContain(Path root, String relativePom, String... forbidden) {
		var path = root.resolve(relativePom.replace('/', File.separatorChar));
		assertTrue(Files.exists(path), "Expected pom file to exist: " + path);

		List<String> references = new ArrayList<>();
		try {
			var factory = DocumentBuilderFactory.newInstance();
			Document document = factory.newDocumentBuilder().parse(path.toFile());
			NodeList dependencyNodes = document.getElementsByTagName("dependency");
			for (int i = 0; i < dependencyNodes.getLength(); i++) {
				var element = (Element) dependencyNodes.item(i);
				var groupId = textOf(element, "groupId");
				var artifactId = textOf(element, "artifactId");
				references.add(groupId + ":" + artifactId);
			}
		} catch (Exception e) {
			throw new IllegalStateException("Failed to parse " + path, e);
		}

		var lowerForbidden = List.of(forbidden).stream().map(f -> f.toLowerCase(Locale.ROOT)).toList();
		var offenders = references.stream().filter(reference -> lowerForbidden.stream()
				.anyMatch(term -> reference.toLowerCase(Locale.ROOT).contains(term))).toList();

		assertTrue(offenders.isEmpty(), relativePom + " has forbidden dependencies: " + String.join(", ", offenders));
	}

	private static String textOf(Element parent, String tagName) {
		var nodes = parent.getElementsByTagName(tagName);
		return nodes.getLength() > 0 ? nodes.item(0).getTextContent() : "";
	}

	private static Path findRepositoryRoot() {
		var directory = Path.of("").toAbsolutePath();

		while (directory != null) {
			if (Files.exists(directory.resolve("Foundgine.sln"))) {
				return directory;
			}
			directory = directory.getParent();
		}

		throw new IllegalStateException("Could not locate Foundgine repository root.");
	}
}