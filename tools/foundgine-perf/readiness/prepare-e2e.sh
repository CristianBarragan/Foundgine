#!/usr/bin/env bash
# Make a patched COPY of the e2e example that builds against the Foundgine checkout under test.
# usage: prepare-e2e.sh <E2E_DIR> <FOUNDGINE_DIR> <WORK_DIR>      (prints the path of the prepared copy)
# Why a copy: the originals are never edited, and four things in the shipped example prevent testing any other Foundgine revision:
#  1. scripts/pack-foundgine.sh only accepts commit dd18ccf9fd05 and version 3.3.6-ci.<12 chars of that commit>;
#  2. crm/Directory.Build.props defaults FoundgineVersion to 3.3.5, so scripts/demo.sh asks pack-foundgine.sh for 3.3.5 and is rejected;
#  3. infrastructure/docker/erp.Dockerfile resolves foundgine-core/-providers 3.3.5 from Maven Central, i.e. NOT the checkout under test;
#  4. scripts/demo.sh tears the stack down on exit unless --keep is given (use --keep before running journeys).
set -euo pipefail
E2E=$(cd "${1:?E2E_DIR}" && pwd); FG=$(cd "${2:?FOUNDGINE_DIR}" && pwd); WORK=${3:?WORK_DIR}
COMMIT=${FOUNDGINE_COMMIT:-$(git -C "$FG" rev-parse HEAD 2>/dev/null)} || { echo "ERROR: $FG is not a git checkout; set FOUNDGINE_COMMIT" >&2; exit 2; }
VERSION="3.3.6-ci.${COMMIT:0:12}"
OUT="$WORK/e2e"; rm -rf "$OUT"; mkdir -p "$OUT"
tar -C "$E2E" --exclude=.git --exclude=bin --exclude=obj --exclude=target -cf - . | tar -C "$OUT" -xf -
find "$OUT" \( -name '*.sh' -o -name '*.py' -o -name '*.sql' -o -name '*.yml' -o -name '*.xml' -o -name '*.props' -o -name 'Dockerfile*' -o -name '*.Dockerfile' \) -type f -exec sed -i 's/\r$//' {} +
sed -i "s/^EXPECTED_COMMIT=.*/EXPECTED_COMMIT=\"$COMMIT\"/" "$OUT/scripts/pack-foundgine.sh"
sed -i "s|\(<FoundgineVersion Condition=\"'\$(FoundgineVersion)' == ''\">\)[^<]*|\1$VERSION|" "$OUT/crm/Directory.Build.props"
sed -i "s|<foundgine.version>[^<]*</foundgine.version>|<foundgine.version>$VERSION</foundgine.version>|" "$OUT/erp/pom.xml"
grep -q "$VERSION" "$OUT/crm/Directory.Build.props" || { echo "ERROR: could not set FoundgineVersion in Directory.Build.props" >&2; exit 2; }
grep -q "$VERSION" "$OUT/erp/pom.xml" || { echo "ERROR: could not set foundgine.version in erp/pom.xml" >&2; exit 2; }
# Java side: build the checkout's artifacts and hand them to the ERP image build as a pre-seeded local Maven repo.
mvn -B -q -f "$FG/src/java/pom.xml" -Drevision="$VERSION" -DskipTests -Dgpg.skip=true -pl foundgine-core,foundgine-providers -am install >&2
mkdir -p "$OUT/m2-local/io/github"; cp -r "$HOME/.m2/repository/io/github/cristianbarragan" "$OUT/m2-local/io/github/"
sed -i 's|^WORKDIR /src$|WORKDIR /src\nCOPY m2-local /root/.m2/repository|' "$OUT/infrastructure/docker/erp.Dockerfile"
grep -q "m2-local" "$OUT/infrastructure/docker/erp.Dockerfile" || { echo "ERROR: could not patch erp.Dockerfile" >&2; exit 2; }
printf 'foundgine_commit=%s\nfoundgine_version=%s\ne2e_commit=%s\n' "$COMMIT" "$VERSION" "$(git -C "$E2E" rev-parse HEAD 2>/dev/null || echo unknown)" > "$OUT/.readiness-refs"
echo "$OUT"
