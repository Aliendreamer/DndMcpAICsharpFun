#!/usr/bin/env bash
# One-command chat-quality eval against the running app. Uses the GPU for ~15-20 minutes.
#   Tools/ModelEval/run-full.sh                          # all questions, 3 runs each
#   Tools/ModelEval/run-full.sh --runs 1                 # quick look (~6 minutes)
#   Tools/ModelEval/run-full.sh --categories spell,lore  # some categories
# Any ModelEval option can be added. The admin key is read inside the app container and never leaves it.
# The report lands in Tools/ModelEval/reports/. Exit: 0 OK, 2 a category regressed, 1 setup trouble.
set -euo pipefail
cd "$(dirname "$0")/../.."
app=${APP_CONTAINER:-dndmcpaicsharpfun-app-1}
out=$(mktemp -d)
remote=/tmp/model-eval-$$
cleanup() {
  rm -rf "${out:?}"
  docker exec -u 0 "$app" rm -rf "$remote" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "Building the eval tool..."
dotnet publish Tools/ModelEval -c Release -o "$out/bin" -v q --nologo >"$out/build.log" 2>&1 || { tail -20 "$out/build.log"; exit 1; }
# A publish carries the decrypted settings along; the tool does not need them.
rm -rf "$out/bin/Config"
baseline=$(ls -1 Tools/ModelEval/reports/*-full.json 2>/dev/null | tail -1 || true)
docker exec "$app" mkdir -p "$remote/reports"
docker cp -q "$out/bin" "$app:$remote/bin"
docker cp -q Tools/ModelEval/cases/manual.json "$app:$remote/manual.json"
args=(--mode full --canonical-dir /books/canonical --manual "$remote/manual.json" --reports-dir "$remote/reports")
if [ -n "$baseline" ]; then
  docker cp -q "$baseline" "$app:$remote/baseline.json"
  args+=(--baseline "$remote/baseline.json")
  echo "Comparing with $(basename "$baseline")"
fi
code=0
docker exec -w /app "$app" sh -c '
  key() { sed -n "/\"Admin\"/,/}/s/.*\"ApiKey\" *: *\"\([^\"]*\)\".*/\1/p" "$1" | head -1; }
  ADMIN_API_KEY=$(key "/app/Config/appsettings.${ASPNETCORE_ENVIRONMENT:-Production}.json")
  [ -n "$ADMIN_API_KEY" ] || ADMIN_API_KEY=$(key /app/Config/appsettings.json)
  export ADMIN_API_KEY
  dll=$1; shift
  exec dotnet "$dll" "$@"' sh "$remote/bin/ModelEval.dll" "${args[@]}" "$@" || code=$?
report=$(docker exec "$app" sh -c "ls -1 $remote/reports 2>/dev/null | tail -1")
if [ -n "$report" ]; then
  docker cp -q "$app:$remote/reports/$report" "Tools/ModelEval/reports/$report"
  echo "Saved Tools/ModelEval/reports/$report"
fi
exit "$code"
