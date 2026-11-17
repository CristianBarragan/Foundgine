#!/usr/bin/env bash
# Make a patched COPY of the e2e example that builds against the Foundgine checkout under test.
# usage: prepare-e2e.sh <E2E_DIR> <FOUNDGINE_DIR> <WORK_DIR>      (prints the path of the prepared copy)
# Why a copy: the originals are never edited, and these things in the shipped example prevent testing any other Foundgine revision:
#  1. scripts/pack-foundgine.sh only accepts commit dd18ccf9fd05 and version 3.3.6-ci.<12 chars of that commit>;
#  2. crm/Directory.Build.props defaults FoundgineVersion to 3.3.5, so scripts/demo.sh asks pack-foundgine.sh for 3.3.5 and is rejected;
#  3. infrastructure/docker/erp.Dockerfile resolves foundgine-core/-providers 3.3.5 from Maven Central, i.e. NOT the checkout under test;
#  4. scripts/demo.sh tears the stack down on exit unless --keep is given (use --keep before running journeys);
#  5. the checkout has no Foundgine.Extensions package any more: it is split into Foundgine.Extensions.Temporal and .Keycloak
#     (the CRM and pack-foundgine.sh still reference the old single package);
#  6. Spring Boot 4 moved Flyway auto-configuration into spring-boot-starter-flyway; the ERP only has flyway-core, so the schema is never created.
#  7. the CRM Dockerfiles never copy crm/Directory.Build.props, so FoundgineVersion is empty inside the image (NU1015).
#  8. the crm-api/erp health probes need bash's /dev/tcp but run under dash, so they can never pass.
#  9. demo.sh's `up --wait` fails on the one-shot erp-seed job exiting (0) with newer Compose versions.
# 10. the realm import has no built-in 'basic' client scope, so Keycloak 26 issues tokens without 'sub' (CRM: 401 Token has no subject).
# stdout is captured by the caller as the path of the copy: every message below goes to stderr.
set -euo pipefail
E2E=$(cd "${1:?E2E_DIR}" && pwd); FG=$(cd "${2:?FOUNDGINE_DIR}" && pwd); WORK=${3:?WORK_DIR}
COMMIT=${FOUNDGINE_COMMIT:-$(git -C "$FG" rev-parse HEAD 2>/dev/null)} || { echo "ERROR: $FG is not a git checkout; set FOUNDGINE_COMMIT" >&2; exit 2; }
VERSION="3.3.6-ci.${COMMIT:0:12}"
OUT="$WORK/e2e"; rm -rf "$OUT"; mkdir -p "$OUT"
tar -C "$E2E" --exclude=.git --exclude=bin --exclude=obj --exclude=target -cf - . | tar -C "$OUT" -xf -
find "$OUT" \( -name '*.sh' -o -name '*.py' -o -name '*.sql' -o -name '*.yml' -o -name '*.xml' -o -name '*.props' -o -name 'Dockerfile*' -o -name '*.Dockerfile' -o -name '*.json' -o -name '.env*' -o -name '*.properties' -o -name '*.conf' -o -name '*.csproj' \) -type f -exec sed -i 's/\r$//' {} +
sed -i "s/^EXPECTED_COMMIT=.*/EXPECTED_COMMIT=\"$COMMIT\"/" "$OUT/scripts/pack-foundgine.sh"
sed -i "s|\(<FoundgineVersion Condition=\"'\$(FoundgineVersion)' == ''\">\)[^<]*|\1$VERSION|" "$OUT/crm/Directory.Build.props"
sed -i "s|<foundgine.version>[^<]*</foundgine.version>|<foundgine.version>$VERSION</foundgine.version>|" "$OUT/erp/pom.xml"
grep -q "$VERSION" "$OUT/crm/Directory.Build.props" || { echo "ERROR: could not set FoundgineVersion in Directory.Build.props" >&2; exit 2; }
grep -q "$VERSION" "$OUT/erp/pom.xml" || { echo "ERROR: could not set foundgine.version in erp/pom.xml" >&2; exit 2; }

# (5) Foundgine.Extensions -> Foundgine.Extensions.Temporal + Foundgine.Extensions.Keycloak (only if the checkout is split)
if [ -d "$FG/src/csharp/Foundgine.Extensions.Temporal" ] && [ -d "$FG/src/csharp/Foundgine.Extensions.Keycloak" ] && [ ! -d "$FG/src/csharp/Foundgine.Extensions" ]; then
  while IFS= read -r f; do
    sed -i 's|^\( *\)<PackageReference Include="Foundgine\.Extensions" Version="\([^"]*\)" */>|\1<PackageReference Include="Foundgine.Extensions.Temporal" Version="\2" />\n\1<PackageReference Include="Foundgine.Extensions.Keycloak" Version="\2" />|' "$f"
  done < <(grep -rl --include='*.csproj' 'Include="Foundgine\.Extensions"' "$OUT/crm" || true)
  sed -i 's|^    Foundgine\.Extensions$|    Foundgine.Extensions.Temporal\n    Foundgine.Extensions.Keycloak|; s|^    "Foundgine\.Extensions",$|    "Foundgine.Extensions.Temporal",\n    "Foundgine.Extensions.Keycloak",|' "$OUT/scripts/pack-foundgine.sh"
  if grep -rq --include='*.csproj' 'Include="Foundgine\.Extensions"' "$OUT/crm" || ! grep -q 'Foundgine.Extensions.Keycloak' "$OUT/scripts/pack-foundgine.sh"; then
    echo "ERROR: could not patch the split Foundgine.Extensions packages" >&2; exit 2
  fi
  echo "patched e2e copy for split Foundgine.Extensions.{Temporal,Keycloak}" >&2
fi

# (6) Spring Boot 4: Flyway auto-configuration lives in spring-boot-starter-flyway
if ! grep -q 'spring-boot-starter-flyway' "$OUT/erp/pom.xml"; then
  sed -i '0,/<dependencies>/s||<dependencies>\n        <dependency>\n            <groupId>org.springframework.boot</groupId>\n            <artifactId>spring-boot-starter-flyway</artifactId>\n        </dependency>\n|' "$OUT/erp/pom.xml"
  grep -q 'spring-boot-starter-flyway' "$OUT/erp/pom.xml" || { echo "ERROR: could not add spring-boot-starter-flyway to erp/pom.xml" >&2; exit 2; }
  echo "patched erp/pom.xml: added spring-boot-starter-flyway" >&2
fi

# (7) The CRM Dockerfiles copy only crm/src, so crm/Directory.Build.props (which defines FoundgineVersion) is missing in the image build
#     and every Version="$(FoundgineVersion)" restores as empty (NU1015). Copy it in next to crm/src.
for df in "$OUT"/infrastructure/docker/crm-*.Dockerfile "$OUT"/infrastructure/docker/crm-*.Dockerfile.*; do
  [ -f "$df" ] || continue
  grep -q 'Directory.Build.props' "$df" || sed -i 's|^COPY crm/src crm/src$|COPY crm/Directory.Build.props crm/Directory.Build.props\nCOPY crm/src crm/src|' "$df"
  grep -q 'crm/Directory.Build.props' "$df" || { echo "ERROR: could not patch $(basename "$df") to copy crm/Directory.Build.props" >&2; exit 2; }
done
echo "patched crm Dockerfiles: COPY crm/Directory.Build.props" >&2

# (8) The crm-api and erp health probes use bash's /dev/tcp through CMD-SHELL, i.e. /bin/sh. On the Debian/Ubuntu-based aspnet and
#     temurin images sh is dash, which has no /dev/tcp ("cannot create /dev/tcp/...: Directory nonexistent"), so the probe can never
#     pass and the services stay unhealthy although they run. Run those probes with bash (present in both images).
DEMO="$OUT/infrastructure/docker-compose.demo.yml"
sed -Ei 's#\["CMD-SHELL", "exec 3<>/dev/tcp/127\.0\.0\.1/(5000|8082)#["CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/\1#' "$DEMO"
[ "$(grep -c '"CMD", "bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/' "$DEMO")" = 2 ] || { echo "ERROR: could not patch the crm-api/erp health probes in docker-compose.demo.yml" >&2; exit 2; }
echo "patched health probes: crm-api and erp run /dev/tcp checks with bash" >&2

# (9) demo.sh runs `up -d --build --wait` over every service, including the one-shot erp-seed job. Newer Compose versions treat a
#     service that exits (even with 0) as a --wait failure ("container ...-erp-seed-1 exited (0)"), so demo.sh aborts before the journey.
#     Wait only for the long-running services (temporal-ui, a browser dashboard nothing depends on, is skipped too); the e2e runner starts erp-seed itself (depends_on: service_completed_successfully).
sed -i '/up -d --build --wait$/{
i mapfile -t SVCS < <("${DC[@]}" config --services | grep -vxE "erp-seed|e2e|temporal-ui")
s|up -d --build --wait$|up -d --build --wait "${SVCS[@]}"|
}' "$OUT/scripts/demo.sh"
grep -q 'wait "${SVCS\[@\]}"' "$OUT/scripts/demo.sh" && bash -n "$OUT/scripts/demo.sh" || { echo "ERROR: could not patch scripts/demo.sh to skip the one-shot erp-seed in up --wait" >&2; exit 2; }
echo "patched demo.sh: up --wait excludes the one-shot erp-seed" >&2

# (10) Keycloak 26 does not create its built-in client scopes when the realm import defines its own clientScopes ("Referenced client scope
#      'basic' doesn't exist. Ignoring"). Since Keycloak 25 the "sub" claim is issued by the built-in "basic" scope, so every token lacks "sub"
#      and the CRM answers 401 "Token has no subject.". Add "basic" (sub, auth_time) and "profile" (preferred_username, the name claim the
#      Foundgine Keycloak extension uses) to the realm import, only if the realm does not define them already.
python3 - "$OUT/infrastructure/keycloak/enterprise-realm.json" <<'PY' >&2 || { echo "ERROR: could not patch enterprise-realm.json" >&2; exit 2; }
import json, sys
path = sys.argv[1]
realm = json.load(open(path, encoding="utf-8"))
scopes = realm.setdefault("clientScopes", [])
have = {sc["name"] for sc in scopes}
def mapper(name, kind, **config):
    return {"name": name, "protocol": "openid-connect", "protocolMapper": kind, "consentRequired": False, "config": config}
add = []
if "basic" not in have:
    add.append({"name": "basic", "description": "OpenID Connect scope for add all basic claims to the token", "protocol": "openid-connect",
        "attributes": {"include.in.token.scope": "false", "display.on.consent.screen": "false"},
        "protocolMappers": [
            mapper("sub", "oidc-sub-mapper", **{"access.token.claim": "true", "introspection.token.claim": "true"}),
            mapper("auth_time", "oidc-usersessionmodel-note-mapper", **{"user.session.note": "AUTH_TIME", "id.token.claim": "true",
                   "introspection.token.claim": "true", "access.token.claim": "true", "claim.name": "auth_time", "jsonType.label": "long"})]})
if "profile" not in have:
    add.append({"name": "profile", "description": "OpenID Connect built-in scope: profile", "protocol": "openid-connect",
        "attributes": {"include.in.token.scope": "true", "display.on.consent.screen": "true"},
        "protocolMappers": [
            mapper("username", "oidc-usermodel-attribute-mapper", **{"user.attribute": "username", "claim.name": "preferred_username",
                   "jsonType.label": "String", "id.token.claim": "true", "access.token.claim": "true", "introspection.token.claim": "true"})]})
scopes.extend(add)
json.dump(realm, open(path, "w", encoding="utf-8"), indent=2)
print("patched enterprise-realm.json: added client scopes " + (", ".join(sc["name"] for sc in add) or "(none needed)"))
PY

# Java side: build the checkout's artifacts and hand them to the ERP image build as a pre-seeded local Maven repo.
mvn -B -q -f "$FG/src/java/pom.xml" -Drevision="$VERSION" -DskipTests -Dgpg.skip=true -pl foundgine-core,foundgine-providers -am install >&2
mkdir -p "$OUT/m2-local/io/github"; cp -r "$HOME/.m2/repository/io/github/cristianbarragan" "$OUT/m2-local/io/github/"
sed -i 's|^WORKDIR /src$|WORKDIR /src\nCOPY m2-local /root/.m2/repository|' "$OUT/infrastructure/docker/erp.Dockerfile"
grep -q "m2-local" "$OUT/infrastructure/docker/erp.Dockerfile" || { echo "ERROR: could not patch erp.Dockerfile" >&2; exit 2; }
printf 'foundgine_commit=%s\nfoundgine_version=%s\ne2e_commit=%s\n' "$COMMIT" "$VERSION" "$(git -C "$E2E" rev-parse HEAD 2>/dev/null || echo unknown)" > "$OUT/.readiness-refs"
echo "$OUT"