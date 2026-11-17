"""Measure a disposable Stage 2 data set; requires running local Compose infrastructure.

Creates a unique database/broker vhost, never modifies an existing database.
Results and protection keys stay under ignored .local/. Keeps fixtures for inspection.
Image rows model reference-query volume; this check does not measure object transfer.
"""

import argparse
import concurrent.futures
import json
import math
import os
from pathlib import Path
import platform
import re
import shutil
import subprocess
import time
import urllib.parse
import urllib.request
import uuid


ROOT = Path(__file__).resolve().parents[1]


def run(args, *, data=None, env=None, log=None):
    result = subprocess.run(args, cwd=ROOT, input=data, text=True, encoding="utf-8",
                            capture_output=True, env=env, check=False)
    if log is not None or result.returncode:
        log = log or ROOT / ".local/issue14" / ("command-failure-" + uuid.uuid4().hex + ".log")
        log.parent.mkdir(parents=True, exist_ok=True)
        log.write_text(result.stdout + "\n" + result.stderr, encoding="utf-8")
    if result.returncode:
        raise RuntimeError(f"{args[0]} failed ({result.returncode}); diagnostic: {log}")
    return result.stdout.strip()


def sql(database, statement):
    return run(["docker", "compose", "exec", "-T", "postgres", "psql", "-X", "-qAt",
                "-v", "ON_ERROR_STOP=1", "-U", "cityportal", "-d", database], data=statement)


def fetch(url):
    with urllib.request.urlopen(url, timeout=30) as response:
        return response.status, response.headers, response.read()


def wait(url):
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        try:
            fetch(url)
            return
        except (OSError, TimeoutError):
            time.sleep(0.5)
    raise RuntimeError(f"Service did not become ready: {url}")


def measure(url, count, readers, target):
    def sample(_):
        start = time.perf_counter()
        status, headers, body = fetch(url)
        if status != 200 or "no-store" not in headers.get("Cache-Control", ""):
            raise RuntimeError("Expected successful no-store public response.")
        return (time.perf_counter() - start) * 1000, len(body)

    # Warm the same concurrency as the measurement; retain cold-burst evidence.
    with concurrent.futures.ThreadPoolExecutor(max_workers=readers) as pool:
        warmup = list(pool.map(sample, range(readers * 3)))
    start = time.perf_counter()
    with concurrent.futures.ThreadPoolExecutor(max_workers=readers) as pool:
        samples = list(pool.map(sample, range(count)))
    elapsed = time.perf_counter() - start
    times = sorted(item[0] for item in samples)
    p95 = times[math.ceil(len(times) * .95) - 1]
    return {"requests": count, "readers": readers, "errors": 0,
            "warmup_requests": len(warmup), "warmup_max_ms": round(max(item[0] for item in warmup), 2),
            "p50_ms": round(times[len(times) // 2], 2), "p95_ms": round(p95, 2),
            "max_ms": round(times[-1], 2), "requests_per_second": round(count / elapsed, 2),
            "max_bytes": max(item[1] for item in samples), "target_p95_ms": target,
            "passed": p95 <= target}


def memory(processes):
    if os.name == "nt":
        ids = ",".join(str(process.pid) for process in processes)
        return json.loads(run(["powershell", "-NoProfile", "-Command",
                               f"Get-Process -Id {ids} | Select-Object Id,WorkingSet64,PrivateMemorySize64 | ConvertTo-Json"]))
    return run(["ps", "-o", "pid=,rss=", "-p", ",".join(str(process.pid) for process in processes)])


def seed(database):
    sql(database, '''
BEGIN;
INSERT INTO identity."SiteConfiguration" ("Id") VALUES (1);
INSERT INTO identity."AspNetUsers" ("Id", "DisplayName", "Email", "NormalizedEmail", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
VALUES ('14000000-0000-0000-0000-000000000001', 'CapacityAuthor', 'capacity@cityportal.test', 'CAPACITY@CITYPORTAL.TEST', true, false, false, false, 0);
INSERT INTO news."Taxonomy" ("Id", "Kind", "Name", "NormalizedName", "Archived", "Version")
SELECT md5('category' || n)::uuid, 'categories', 'Category ' || n, 'CATEGORY ' || n, false, 1 FROM generate_series(1,100) n;
INSERT INTO news."Taxonomy" ("Id", "Kind", "Name", "NormalizedName", "Archived", "Version")
SELECT md5('tag' || n)::uuid, 'tags', 'Tag ' || n, 'TAG ' || n, false, 1 FROM generate_series(1,1000) n;
INSERT INTO media."Images" ("Id", "OwnerId", "Name", "State", "Width", "Height", "Version", "CreatedAtUtc", "NextAttemptAtUtc", "Attempts", "EverAttached")
SELECT md5('image' || n)::uuid, '14000000-0000-0000-0000-000000000001', 'Reference fixture ' || n, 'Ready', 640, 480, 1, now(), now(), 0, true FROM generate_series(1,10) n;
INSERT INTO news."Articles" ("Id", "AuthorId", "Version", "PublicationState", "WorkingRevisionId", "PublishedRevisionId", "LastSubmittedRevisionId", "FirstPublishedAtUtc", "CreatedAtUtc", "UpdatedAtUtc", "Featured")
SELECT md5('article' || n)::uuid, '14000000-0000-0000-0000-000000000001', 6, 'Published', md5('revision' || n)::uuid, md5('revision' || n)::uuid, md5('revision' || n)::uuid,
now() - n * interval '1 minute', now() - n * interval '1 minute', now(), n <= 5 FROM generate_series(1,10000) n;
INSERT INTO news."Revisions" ("Id", "ArticleId", "State", "ContentJson", "Title", "PlainText", "PreviewHtml", "Retained", "CreatedAtUtc", "SubmittedAtUtc")
SELECT md5('revision' || n)::uuid, md5('article' || n)::uuid, 'Published',
jsonb_build_object('Title', 'Городские новости ' || n, 'Summary', 'Новости транспорта и культуры города', 'SchemaVersion', 1,
'Body', jsonb_build_object('type','doc','content',jsonb_build_array(jsonb_build_object('type','paragraph','content',jsonb_build_array(jsonb_build_object('type','text','text',repeat('Развитие города, транспорт и культура. ',100)))))),
'CategoryId', md5('category' || ((n-1)%100+1))::uuid,
'TagIds', jsonb_build_array(md5('tag' || ((n-1)%1000+1))::uuid), 'Cover', null, 'Gallery', '[]'::jsonb, 'UsesOthers', false, 'Sources', '[]'::jsonb, 'SeoTitle', '', 'SeoDescription', ''),
'Городские новости ' || n, repeat('Развитие города, транспорт и культура. ',100), '<p>Развитие города, транспорт и культура.</p>', true, now(), now() FROM generate_series(1,10000) n;
INSERT INTO media."References" ("RevisionId", "ImageId", "AuthorId", "ReviewerId", "Submitted", "Public", "Current")
SELECT md5('revision' || a)::uuid, md5('image' || i)::uuid, '14000000-0000-0000-0000-000000000001', NULL, true, true, true FROM generate_series(1,10000) a CROSS JOIN generate_series(1,10) i;
UPDATE news."HomepageSelections" SET "FirstCategoryId"=md5('category1')::uuid, "SecondCategoryId"=md5('category2')::uuid;
COMMIT;
ANALYZE;
''')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--api-port", type=int, default=5380)
    parser.add_argument("--ssr-port", type=int, default=4380)
    parser.add_argument("--requests", type=int, default=80)
    parser.add_argument("--serve", action="store_true", help="Keep isolated API/SSR running after measurement")
    parser.add_argument("--reuse", type=Path, help="Reuse a fixture directory created by this script")
    args = parser.parse_args()
    if args.requests < 20:
        parser.error("Use at least 20 samples for p95.")
    if args.reuse:
        output = args.reuse.resolve()
        if output.parent != (ROOT / ".local/issue14").resolve() or not re.fullmatch(r"capacity_[0-9a-f]{32}", output.name):
            parser.error("Reuse only this script's isolated .local/issue14/capacity_<uuid> fixture.")
        name = output.name
    else:
        name = "capacity_" + uuid.uuid4().hex
        output = ROOT / ".local" / "issue14" / name
        output.mkdir(parents=True)
    secrets = {}
    for line in (ROOT / ".env").read_text(encoding="utf-8-sig").splitlines():
        if "=" in line and not line.startswith("#"):
            key, value = line.split("=", 1)
            secrets[key] = value
    dotnet = ROOT / ".local" / "dotnet" / ("dotnet.exe" if os.name == "nt" else "dotnet")
    if not dotnet.exists():
        dotnet = Path(shutil.which("dotnet") or "dotnet")
    runtime = output / "api"
    shutil.copytree(ROOT / "src/CityPortal.Api/bin/Release/net10.0", runtime, dirs_exist_ok=True)
    if not args.reuse:
        run(["docker", "compose", "exec", "-T", "postgres", "createdb", "-U", "cityportal", name])
        run(["docker", "compose", "exec", "-T", "rabbitmq", "rabbitmqctl", "add_vhost", name])
    user = secrets.get("RABBITMQ_USER", "cityportal")
    run(["docker", "compose", "exec", "-T", "rabbitmq", "rabbitmqctl", "set_permissions", "-p", name, user, ".*", ".*", ".*"])
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), ASPNETCORE_ENVIRONMENT="Development",
               ASPNETCORE_CONTENTROOT=str(ROOT / "src/CityPortal.Api"), Installation__Enabled="true",
               ConnectionStrings__Database=f"Host=localhost;Database={name};Username=cityportal;Password={secrets['POSTGRES_PASSWORD']};GSS Encryption Mode=Disable",
               ConnectionStrings__Messaging=f"amqp://{user}:{secrets['RABBITMQ_PASSWORD']}@localhost:5672/{name}",
               Security__DataProtectionKeysPath=str(output / "keys"), City__TimeZone="Asia/Yekaterinburg",
               Media__Endpoint="http://localhost:9000", Media__AllowHttp="true",
               Media__AccessKey=secrets["MEDIA_ACCESS_KEY"], Media__SecretKey=secrets["MEDIA_SECRET_KEY"], Media__Bucket=name.replace('_', '-'))
    commands = [str(dotnet), str(runtime / "CityPortal.Api.dll")]
    for attempt in (1, 2):
        run(commands + ["--migrate"], env=env, log=output / f"migration-{attempt}.log")
    if not args.reuse:
        seed(name)
    print("Prepared isolated fixture " + name, flush=True)
    processes = []
    handles = []
    try:
        api = f"http://127.0.0.1:{args.api_port}"
        ssr = f"http://127.0.0.1:{args.ssr_port}"
        for command, settings, filename in [
            (commands + ["--urls", api], env, "api.log"),
            ([shutil.which("node") or "node", str(ROOT / "frontend/dist/city-portal/server/server.mjs")],
             dict(os.environ, API_INTERNAL_ORIGIN=api, PORT=str(args.ssr_port), HOST="127.0.0.1"), "ssr.log")]:
            handle = open(output / filename, "w", encoding="utf-8")
            handles.append(handle)
            processes.append(subprocess.Popen(command, cwd=ROOT, env=settings, stdout=handle, stderr=handle))
        wait(api + "/health/ready")
        wait(ssr + "/news")
        article = json.loads(fetch(api + "/api/news/articles")[2])["items"][0]
        category = json.loads(fetch(api + "/api/news/categories")[2])["items"][0]["id"]
        tag = json.loads(fetch(api + "/api/news/tags")[2])["items"][0]["id"]
        routes = {"feed": "/api/news/articles", "deep_page": "/api/news/articles?page=500",
                  "search": "/api/news/articles?query=" + urllib.parse.quote("города"),
                  "combined": f"/api/news/articles?categoryId={category}&tagId={tag}",
                  "homepage": "/api/news/homepage", "tags": "/api/news/tags", "related": f"/api/news/articles/{article['id']}/related"}
        report = {"database": name, "commit": run(["git", "rev-parse", "HEAD"]),
                  "environment": {"host": platform.platform(), "cpu_count": os.cpu_count(),
                                  "docker": run(["docker", "info", "--format", "{{.ServerVersion}} {{.NCPU}} CPUs {{.MemTotal}} bytes"]),
                                  "postgres": sql(name, "SELECT version();"),
                                  "sdk": run([str(dotnet), "--version"]), "node": run(["node", "--version"])},
                  "counts": sql(name, '''SELECT json_build_object('articles',(SELECT count(*) FROM news."Articles"),'references',(SELECT count(*) FROM media."References"),'categories',(SELECT count(*) FROM news."Taxonomy" WHERE "Kind"='categories'),'tags',(SELECT count(*) FROM news."Taxonomy" WHERE "Kind"='tags'));'''),
                  "measurements": {}}
        report["memory_before"] = memory(processes)
        for label, route in routes.items():
            report["measurements"][label] = measure(api + route, args.requests, 8, 500)
            print(label + " p95_ms=" + str(report["measurements"][label]["p95_ms"]), flush=True)
        for label, route in {"ssr_home": "/", "ssr_list": "/news", "ssr_article": article["canonicalUrl"]}.items():
            report["measurements"][label] = measure(ssr + route, args.requests, 8, 1000)
        report["memory_after"] = memory(processes)
        # Capture actual SQL generated by the production EF queries from the local API log separately.
        plans = {}
        base = '''FROM news."Articles" a JOIN news."Revisions" r ON a."PublishedRevisionId"=r."Id" WHERE a."PublicationState"='Published' AND a."DeletedAtUtc" IS NULL'''
        for label, query in {
            "feed": 'SELECT a."Id",r."Title" ' + base + ' ORDER BY a."FirstPublishedAtUtc" DESC,a."Id" LIMIT 20',
            "search": 'SELECT a."Id",r."Title" ' + base + ''' AND r."SearchVector" @@ plainto_tsquery('russian','города') ORDER BY ts_rank(r."SearchVector",plainto_tsquery('russian','города')) DESC,a."FirstPublishedAtUtc" DESC,a."Id" LIMIT 20''',
            "image_visibility": '''SELECT EXISTS(SELECT 1 FROM media."References" WHERE "ImageId"=md5('image1')::uuid AND "Public")'''
            ,"tags": '''SELECT t."Id",t."Name" FROM news."Taxonomy" t WHERE t."Kind"='tags' AND EXISTS (SELECT 1 FROM news."Articles" a JOIN news."Revisions" r ON a."PublishedRevisionId"=r."Id" WHERE a."PublicationState"='Published' AND a."DeletedAtUtc" IS NULL AND r."Tags" ?| ARRAY[t."Id"::text]) ORDER BY t."NormalizedName",t."Id" LIMIT 100'''
        }.items():
            plans[label] = json.loads(sql(name, "EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + query))[0]
        (output / "plans.json").write_text(json.dumps(plans, indent=2), encoding="utf-8")
        (output / "results.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
        metadata = {"database": name, "directory": str(output), "api": api, "ssr": ssr, "pids": [p.pid for p in processes], "article": article}
        (ROOT / ".local/issue14/current.json").write_text(json.dumps(metadata, indent=2), encoding="utf-8")
        print(json.dumps({"directory": str(output), "measurements": report["measurements"]}), flush=True)
        if not all(item["passed"] for item in report["measurements"].values()):
            raise RuntimeError("A measured p95 exceeded its target; inspect results.json.")
        if args.serve:
            print("Isolated API/SSR ready for browser inspection; interrupt to stop.", flush=True)
            while all(process.poll() is None for process in processes):
                time.sleep(1)
    finally:
        for process in processes:
            process.terminate()
        for process in processes:
            try:
                process.wait(timeout=15)
            except subprocess.TimeoutExpired:
                process.kill()
        for handle in handles:
            handle.close()


if __name__ == "__main__":
    main()
