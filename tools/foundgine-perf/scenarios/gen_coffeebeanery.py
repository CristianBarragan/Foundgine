#!/usr/bin/env python3
"""Emit CoffeeBeanery scenario files that replay the exact GraphQL operations of
CoffeeBeanery.LoadTest/Program.cs. Run: python3 scenarios/gen_coffeebeanery.py"""
import json, os, uuid
HERE = os.path.dirname(os.path.abspath(__file__))
QUERY = """query Top50Customers { customer(first: 50) { id customerKey firstName lastName fullName
 customerBankingRelationship { id customerBankingRelationshipKey contract { id contractKey amount
 transaction { id transactionKey amount balance } } } } }"""
MUT = """mutation UpsertCustomer($input: CustomerInput!) { upsertCustomer(input: $input, onConflict: ["CustomerKey"])
 { id customerKey firstName lastName fullName } }"""
# NOTE: LoadTest uses GuidUtility (RFC 4122 v5, URL namespace); uuid5 reproduces the canonical form.
key = lambda i: str(uuid.uuid5(uuid.NAMESPACE_URL, f"coffee-beanery/customer/{i}"))

def write(name, reqs, **extra):
    json.dump({"name": name, "requests": reqs, "sort_lists_by": "id", **extra},
              open(os.path.join(HERE, name + ".json"), "w"), indent=1)

write("cb-query-top50", [{"method": "POST", "path": "", "body": {"query": QUERY, "operationName": "Top50Customers"}}])
write("cb-mutation-b1", [{"method": "POST", "path": "", "body": {"query": MUT, "operationName": "UpsertCustomer",
      "variables": {"input": {"customerKey": key(1), "firstName": "Benchmark", "lastName": "Customer", "fullName": "Benchmark Customer 1"}}}}],
      ignore_keys=["id"])
for n in (10, 50):
    body = "mutation BenchmarkUpsertBatch {\n" + "".join(
        f'  m{i}: upsertCustomer(input: {{ customerKey: "{key(i)}" firstName: "Benchmark" lastName: "Customer" '
        f'fullName: "Benchmark Customer {i}" }}, onConflict: ["CustomerKey"]) {{ id customerKey firstName lastName fullName }}\n'
        for i in range(1, n + 1)) + "}"
    write(f"cb-mutation-b{n}", [{"method": "POST", "path": "", "body": {"query": body, "operationName": "BenchmarkUpsertBatch"}}], ignore_keys=["id"])
