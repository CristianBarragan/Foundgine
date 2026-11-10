#!/usr/bin/env python3
"""Validate the structure and dependency order of the Java JPA parity plan."""
import json
from pathlib import Path

path = Path(__file__).with_name('orm-jpa-parity-plan.json')
data = json.loads(path.read_text(encoding='utf-8'))
assert data['schemaVersion'] == 1
assert data['planVersion'] == 'step70'
assert data['provider'] == 'Jakarta Persistence (JPA)'
milestones = data['milestones']
ids = [m['id'] for m in milestones]
assert ids == [f'JPA-{i}' for i in range(10)]
known = set()
for milestone in milestones:
    assert milestone['name'] and milestone['scope'] and milestone['exitGate']
    assert set(milestone['dependsOn']) <= known, f"{milestone['id']} has a forward or unknown dependency"
    known.add(milestone['id'])
assert {'H2', 'PostgreSQL'} <= set(data['initialDatabaseTargets'])
assert 'native-image' in data['requiredTestLayers']
assert data['currentBaseline']['aggregateCursorPagination'] == 'ci-execution-evidence-gate-added-unverified'
root = path.parents[1]
provider = root / 'foundgine-providers'
test_root = provider / 'src/test/java/com/foundgine/providers/storage/orm/jpa'
assert (test_root / 'JpaOrmProviderIntegrationTest.java').is_file()
assert (test_root / 'JpaBaselineCustomer.java').is_file()
assert (provider / 'src/test/resources/META-INF/persistence.xml').is_file()
assert 'hibernate-core' in (provider / 'pom.xml').read_text(encoding='utf-8')
assert data['latestIncrement']['id'] == 'JPA-5-snapshot-consistency-database-tests'
assert data['latestIncrement']['status'] == 'implemented-unverified'
assert data['currentBaseline']['aggregateCursorSnapshotConsistency'].startswith('repeatable-read-page-session-and-h2-postgresql-concurrency-tests')
fetch_result = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmFetchResult.java'
fetch_result_text = fetch_result.read_text(encoding='utf-8')
assert 'privateOrderValues' in fetch_result_text and 'semanticOnly' in fetch_result_text
assert 'fetchWithPrivateOrderValues' in (provider / 'src/main/java/com/foundgine/providers/storage/orm/IOrmQuerySource.java').read_text(encoding='utf-8')
assert 'tuple.get(r.select().size() + i)' in (provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaOrmQuerySource.java').read_text(encoding='utf-8')
assert 'private_aggregate_order_values_travel_separately_from_semantic_projection' in (provider / 'src/test/java/com/foundgine/providers/storage/orm/OrmExecutionProviderTest.java').read_text(encoding='utf-8')
cursor = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmAggregateCursor.java'
cursor_text = cursor.read_text(encoding='utf-8')
assert 'null-rank' in cursor_text and 'isAfter' in cursor_text and 'decodeTyped' in cursor_text and 'continuationBranches' in cursor_text
cursor_test = provider / 'src/test/java/com/foundgine/providers/storage/orm/OrmAggregateCursorTest.java'
assert cursor_test.is_file()
assert 'keyset_comparison_obeys_nulls_last_and_direction' in cursor_test.read_text(encoding='utf-8')
assert 'typed_decode_coerces_values_and_preserves_null_slots' in cursor_test.read_text(encoding='utf-8')
assert 'continuation_branches_encode_nulls_last_and_mixed_directions' in cursor_test.read_text(encoding='utf-8')
validator = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmPredicateValidator.java'
validator_text = validator.read_text(encoding='utf-8')
assert 'MAX_NODES = 256' in validator_text
assert 'MAX_RELATIONSHIP_DEPTH = 8' in validator_text
assert 'Cyclic ORM predicate graph' in validator_text
assert 'OrmPredicateValidator.validate(clause)' in (provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaOrmQuerySource.java').read_text(encoding='utf-8')
assert 'OrmPredicateValidator.validate(where)' in (provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaOrmMutationSource.java').read_text(encoding='utf-8')
translator = provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaPredicates.java'
translator_text = translator.read_text(encoding='utf-8')
assert 'if (values.isEmpty()) return cb.disjunction();' in translator_text
assert 'in.value(bindings.add(cb, valueType, value))' in translator_text
assert 'return cb.and(cb.isNotNull(path), comparison);' in translator_text
assert 'validateValue(field, value' in translator_text
assert 'field.isOrderable()' in translator_text
assert 'owner.subquery(Integer.class)' in translator_text
assert 'cb.exists(subquery)' in translator_text
assert 'case SOME -> cb.and(childAuthorization, childWhere)' in translator_text
assert 'case NONE -> cb.and(childAuthorization, childWhere)' in translator_text
assert 'case ALL -> cb.and(childAuthorization, cb.not(childWhere))' in translator_text
assert 'cb.not(hasCounterexample)' in translator_text
assert 'AggregateKind.COUNT' in translator_text and 'AggregateKind.MIN' in translator_text and 'cb.greatest' in translator_text
assert 'bindings.add(cb, valueType, aggregate.expected())' in translator_text
assert 'aggregateOrderExpression' in translator_text and 'subquery.select(aggregate)' in translator_text
continuation = provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaAggregateContinuation.java'
continuation_text = continuation.read_text(encoding='utf-8')
assert 'cb.lessThan' in continuation_text and 'cb.greaterThan' in continuation_text
assert 'cb.isNull(key)' in continuation_text and 'cb.isNotNull(key)' in continuation_text
assert 'bindings.add(cb, (Class) type, cursor)' in continuation_text
assert 'record Exists(OrmEntityMapping target, OrmLink link, OrmPredicate authorization, OrmPredicate where)' in (root / 'foundgine-providers/src/main/java/com/foundgine/providers/storage/orm/OrmPredicate.java').read_text(encoding='utf-8')
bindings = provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaParameterBindings.java'
bindings_text = bindings.read_text(encoding='utf-8')
assert 'cb.parameter(type, "fg" + bindings.size())' in bindings_text
assert 'query.setParameter' in bindings_text
test_text = (test_root / 'JpaOrmProviderIntegrationTest.java').read_text(encoding='utf-8')
for token in ['emptyAndNullOnlyInLists', 'explicitNullComparisons', 'notInUsesTwoValuedComplement', 'negatedScalarComparisonsUseBooleanComplement', 'allScalarComparisonOperators', 'nestedAndOrNot', 'mismatchedAndNonOrderable', 'comparisonAndEveryInMemberUseExplicitTypedCriteriaParameters', 'mutationFindBindsPredicateParametersExplicitly', 'correlatedExistsAppliesChildAuthorizationAndBindsNestedParameters', 'inMemoryOracleUsesTheSameCorrelatedChildAuthorizationPredicate', 'relationshipSomeNoneAndAllUseAuthorizationVisibleChildrenOnly', 'inMemoryRelationshipQuantifiersMatchJpaEmptySetAndAuthorizationSemantics', 'nestedRelationshipTraversalIsBoundedAndValidatedBeforeProviderExecution', 'predicateValidatorRejectsOversizedBooleanTrees', 'authorizedRelationshipCountMinAndMaxFiltersMatchInMemorySemantics', 'aggregateOrderingUsesAuthorizedPrivateTupleSlotsAndNullsLast', 'inMemoryAggregateOrderingMatchesAuthorizedMinAndNullPolicy', 'criteriaContinuationCompilesTypedLexicographicPredicateWithBoundValues', 'fetchRequestCompilesAggregateContinuationAgainstPrivateOrderingExpressions']:
    assert token in test_text
for token in ['aggregateCursorPagesHaveNoDuplicatesOrOmissionsAcrossTiesAndNulls', 'aggregateCursorCannotBeReplayedAcrossTenantAuthorization', 'aggregateCursorIsRejectedWhenChildAuthorizationChangesWithinTenant']:
    assert token in test_text
assert 'concatenated pages must equal the complete deterministic ordering exactly once' in test_text
assert 'fetchWithPrivateOrderValues(request)' in test_text
assert 'private aggregate values must stay aligned with semantic rows' in test_text
workflow = root.parents[1] / '.github/workflows/java-jpa-plan.yml'
assert 'postgres:17' in workflow.read_text(encoding='utf-8')
fixture = test_root / 'JpaTypeFixture.java'
assert fixture.is_file()
for token in ['decimalAndBigIntegerValuesRetainExactPrecision', 'enumAndUuidEqualityUseTypedParameters', 'javaTimeTypesSupportEqualityAndDeclaredChronologicalOrdering']:
    assert token in test_text
field_binding = root / 'foundgine-providers/src/main/java/com/foundgine/providers/storage/orm/OrmFieldBinding.java'
field_binding_text = field_binding.read_text(encoding='utf-8')
assert 'type == BigDecimal.class' in field_binding_text and 'type == BigInteger.class' in field_binding_text
evidence_gate = root / 'conformance' / 'verify-jpa-ci-evidence.py'
assert evidence_gate.is_file()
evidence_text = evidence_gate.read_text(encoding='utf-8')
for token in ['aggregateCursorPagesHaveNoDuplicatesOrOmissionsAcrossTiesAndNulls', 'aggregateCursorCannotBeReplayedAcrossTenantAuthorization', 'aggregateCursorIsRejectedWhenChildAuthorizationChangesWithinTenant', 'postgresExecutesTheSameScalarCriteriaContractWhenConfigured', 'postgresExecutesAggregateCursorPagesWithoutDuplicatesOrOmissionsWhenConfigured', 'postgresRejectsAggregateCursorReplayAcrossTenantAndChangedChildAuthorizationWhenConfigured', 'postgresCharacterizesAggregateCursorAnomaliesWhenChildAggregatesChangeBetweenPages', 'h2SnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges', 'postgresSnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges']:
    assert token in evidence_text
workflow_text = workflow.read_text(encoding='utf-8')
assert 'Verify declared Java toolchain' in workflow_text and 'Require H2 and PostgreSQL cursor regression methods to execute' in workflow_text
print(f"Validated {len(milestones)} Java JPA milestones, {len(data['requiredTestLayers'])} test layers, the JPA-0 integration harness, and Step 46 scalar type-family, Step 47 correlated EXISTS, Step 48 quantifiers, Step 49 bounded nested traversal, Step 50 authorized COUNT/MIN/MAX filters, and Step 51 authorized aggregate ordering, Step 52 canonical aggregate cursor codec/null-rank groundwork, Step 53 typed cursor decoding/continuation descriptors, and Step 54 typed JPA Criteria continuation compilation, Step 55 plan-bound cursor envelope, Step 56 private aggregate tuple transport, Step 57 plan-bound private aggregate cursor pipeline, Step 59 interleaved ordered cursor tuples, Step 60 plan-bound fetch-request and next-page cursor adapters, and Step 61 execution-plan-bound aggregate cursor integration, and Step 62 multi-page aggregate cursor integration coverage, Step 63 authorization-bound cursor replay regressions, and Step 64 canonical plan identity dimension regressions, and Step 65 Java CI execution-evidence gating, and Step 66 PostgreSQL aggregate cursor pagination evidence, and Step 67 PostgreSQL authorization-bound cursor replay coverage, and Step 68 concurrent PostgreSQL aggregate data-change characterization, and Step 69 opt-in JPA snapshot page-session contract, and Step 70 H2/PostgreSQL repeatable-read concurrent-write snapshot tests.")

plan_binding = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmAggregateCursorBinding.java'
plan_binding_text = plan_binding.read_text(encoding='utf-8')
assert 'MessageDigest.getInstance("SHA-256")' in plan_binding_text
assert 'different query plan' in plan_binding_text
assert 'decodeTyped' in plan_binding_text
plan_binding_test = provider / 'src/test/java/com/foundgine/providers/storage/orm/OrmAggregateCursorBindingTest.java'
assert plan_binding_test.is_file()
for token in ['bindsCursorToCanonicalPlanIdentityAndDecodesMappedTypes', 'rejectsReplayAgainstDifferentAuthorizationOrSortPlan', 'rejectsMalformedEnvelopeAndWrongArity', 'invalidatesCursorWhenAnyCanonicalPlanDimensionChanges', 'lengthPrefixedCanonicalAtomsCannotCollideOnEmbeddedDelimiters']:
    assert token in plan_binding_test.read_text(encoding='utf-8')

pipeline = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmAggregateCursorPipeline.java'
pipeline_text = pipeline.read_text(encoding='utf-8')
assert 'OrmAggregateCursorBinding.encode' in pipeline_text
assert 'OrmAggregateCursorBinding.decode' in pipeline_text
assert 'primaryKeyValues' in pipeline_text and 'continuationBranches' in pipeline_text
pipeline_test = provider / 'src/test/java/com/foundgine/providers/storage/orm/OrmAggregateCursorPipelineTest.java'
assert pipeline_test.is_file()
for token in ['emitsAndDecodesPrivateAggregateValuesPlusPrimaryKeyTieBreaker', 'preservesNullAggregateAndRejectsNullPrimaryKey', 'rejectsCursorReplayForDifferentPlanAndDirectionArity']:
    assert token in pipeline_test.read_text(encoding='utf-8')

fetch_request = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmFetchRequest.java'
assert 'continuationValues' in fetch_request.read_text(encoding='utf-8')
assert 'JpaAggregateContinuation.compile' in (provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaOrmQuerySource.java').read_text(encoding='utf-8')

assert 'continuationRequest' in (provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmAggregateCursorPipeline.java').read_text(encoding='utf-8')
assert 'createsFetchRequestWithDecodedPrivateContinuationTuple' in (provider / 'src/test/java/com/foundgine/providers/storage/orm/OrmAggregateCursorPipelineTest.java').read_text(encoding='utf-8')

queries = provider / 'src/main/java/com/foundgine/providers/storage/orm/OrmQueries.java'
queries_text = queries.read_text(encoding='utf-8')
assert 'aggregateCursorPlanIdentity' in queries_text
assert 'decodeOrdered(cursor, aggregatePlanIdentity, map, orderBy)' in queries_text
assert 'new OrmFetchRequest(map, read, where, orderBy, skip, take, aggregateContinuation)' in queries_text
assert 'nextPageCursor(aggregatePlanIdentity, map, orderBy' in queries_text
assert 'Aggregate cursor rows and private ordering tuples are misaligned' in queries_text

identity = queries_text[queries_text.index('private static String aggregateCursorPlanIdentity'):queries_text.index('// ------------------------------------------------------------------ the read')]
for token in ['CURSOR_SCHEMA_VERSION', 'root-authorization', 'root-filter', 'root-fields', 'selected-fields', 'child', 'Boolean.toString(term.descending())', 'appendPlanAtom']:
    assert token in identity
assert "target.append(atom.length()).append(':').append(atom)" in identity


snapshot_session = provider / 'src/main/java/com/foundgine/providers/storage/orm/jpa/JpaOrmSnapshotPageSession.java'
snapshot_text = snapshot_session.read_text(encoding='utf-8')
for token in ['REPEATABLE_READ_OR_STRONGER', 'new JpaOrmQuerySource(entityManager)', 'tx.begin()', 'transaction.commit()', 'transaction.rollback()', 'public void close()']:
    assert token in snapshot_text
assert 'snapshotPageSessionOwnsOneActiveEntityManagerTransactionAndRollsBackOnClose' in test_text
assert 'h2SnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges' in test_text
assert 'postgresSnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges' in test_text
assert 'TRANSACTION_REPEATABLE_READ' in test_text
assert 'h2SnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges' in test_text
assert 'postgresSnapshotPageSessionKeepsAggregatePagesStableAcrossConcurrentChildChanges' in test_text
assert 'TRANSACTION_REPEATABLE_READ' in test_text
