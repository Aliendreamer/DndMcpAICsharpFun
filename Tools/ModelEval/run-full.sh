#!/usr/bin/env bash
# One-command chat-quality eval against the running app. Uses the GPU for ~15-20 minutes.
#   Tools/ModelEval/run-full.sh                          # all questions, 3 runs each
#   Tools/ModelEval/run-full.sh --runs 1                 # quick look (~6 minutes)
#   Tools/ModelEval/run-full.sh --categories spell,lore  # some categories
#   Tools/ModelEval/run-full.sh --set reference          # 50 harder questions to improve the chat against
#   Tools/ModelEval/run-full.sh --set crosscheck         # 50 others: never tune on these, only compare
#   Tools/ModelEval/run-full.sh --set all                # both (100 questions; ~2 hours, ~40 minutes with --runs 1)
# Any ModelEval option can be added. The admin key is read inside the app container and never leaves it.
# The report lands in Tools/ModelEval/reports/. Exit: 0 OK, 2 a category regressed, 1 setup trouble.
set -euo pipefail
self=$(realpath "$0")
cd "$(dirname "$self")/../.."
set_name=core
rest=()
while [ $# -gt 0 ]; do
  if [ "$1" = --set ]; then set_name=${2:?--set needs core, reference, crosscheck or all}; shift 2; else rest+=("$1"); shift; fi
done
case "$set_name" in
  core) cases=Tools/ModelEval/cases/manual.json; reports=Tools/ModelEval/reports ;;
  # Hand-written questions only: no generated ones.
  reference|crosscheck) cases=Tools/ModelEval/cases/$set_name.json; reports=Tools/ModelEval/reports/$set_name; rest+=(--count 0) ;;
  all)
    worst=0
    for s in reference crosscheck; do
      # Keep going after infra errors (exit 1, e.g. a few timeouts): they must not cost the other set's report.
      c=0; "$self" --set "$s" ${rest[@]+"${rest[@]}"} || c=$?
      case "$c" in 0) ;; 1) worst=1 ;; *) [ "$worst" -eq 1 ] || worst=$c ;; esac
    done
    exit "$worst" ;;
  *) echo "Unknown --set $set_name (core, reference, crosscheck, all)"; exit 1 ;;
esac
mkdir -p "$reports"
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
baseline=$(ls -1 "$reports"/*-full.json 2>/dev/null | tail -1 || true)
docker exec "$app" mkdir -p "$remote/reports"
docker cp -q "$out/bin" "$app:$remote/bin"
docker cp -q "$cases" "$app:$remote/manual.json"
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
  exec dotnet "$dll" "$@"' sh "$remote/bin/ModelEval.dll" "${args[@]}" ${rest[@]+"${rest[@]}"} || code=$?
report=$(docker exec "$app" sh -c "ls -1 $remote/reports 2>/dev/null | tail -1")
if [ -n "$report" ]; then
  docker cp -q "$app:$remote/reports/$report" "$reports/$report"
  echo "Saved $reports/$report"
fi
exit "$code"
