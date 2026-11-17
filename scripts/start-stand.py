"""Start the local review stack, pgAdmin and five role-specific demo accounts.

Uses the dedicated cityportal_stand database. Passwords persist only in ignored
local files and are printed after every successful launch. Requires Docker and Python.
"""

import argparse
import http.cookiejar
import json
import os
from pathlib import Path
import re
import secrets
import ssl
import subprocess
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
LOCAL = ROOT / ".local/stand"
ROLES = ["RegularUser", "Editor", "Author", "Moderator", "Administrator"]
COMPOSE = ["docker", "compose", "-f", "compose.yaml", "-f", "compose.stand.yaml"]


def command(args, env, *, data=None, capture=False):
    result = subprocess.run(args, cwd=ROOT, env=env, input=data, text=True,
                            encoding="utf-8", capture_output=capture)
    if result.returncode:
        if capture:
            (LOCAL / "command-error.log").write_text(result.stdout + result.stderr, encoding="utf-8")
        raise RuntimeError("Local stand command failed; see console or .local/stand/command-error.log")
    return result.stdout.strip() if capture else None


class Client:
    def __init__(self, origin, ca):
        self.origin = origin
        self.opener = urllib.request.build_opener(
            urllib.request.HTTPCookieProcessor(http.cookiejar.CookieJar()),
            urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=str(ca))))

    def call(self, path, body=None, method=None):
        headers = {"Accept": "application/json"}
        if body is not None:
            headers.update({"Content-Type": "application/json", "X-CSRF-TOKEN": self.call("/api/auth/csrf")["token"]})
        request = urllib.request.Request(self.origin + path, headers=headers,
                                         data=None if body is None else json.dumps(body).encode(), method=method)
        try:
            response = self.opener.open(request, timeout=30)
        except urllib.error.HTTPError as error:
            raise RuntimeError(f"HTTP {error.code} at {path}; local account passwords were preserved.") from None
        with response:
            payload = response.read()
            return json.loads(payload) if payload else None


def wait_for(check, description, timeout=120):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            value = check()
            if value:
                return value
        except (OSError, RuntimeError):
            pass
        time.sleep(1)
    raise RuntimeError("Timed out: " + description)


def confirmation(email):
    with urllib.request.urlopen("http://127.0.0.1:8025/api/v1/messages?limit=100", timeout=10) as response:
        messages = json.load(response)["messages"]
    for summary in messages:
        if any(address["Address"].lower() == email for address in summary["To"]):
            identifier = summary["ID"]
            if not re.fullmatch(r"[A-Za-z0-9_-]+", identifier):
                raise RuntimeError("Invalid Mailpit message identifier")
            with urllib.request.urlopen("http://127.0.0.1:8025/api/v1/message/" + identifier, timeout=10) as response:
                message = json.load(response)
            links = re.findall(r'https://[^\s<>"]+', message["Text"])
            for link in links:
                parsed = urllib.parse.urlsplit(link)
                if parsed.path == "/account/confirm-email":
                    values = urllib.parse.parse_qs(parsed.fragment or parsed.query)
                    return {"linkId": values["linkId"][0], "token": values["token"][0]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-build", action="store_true", help="Reuse already built application images")
    args = parser.parse_args()
    LOCAL.mkdir(parents=True, exist_ok=True)
    env_file = ROOT / ".env"
    if not env_file.exists():
        env_file.write_text((ROOT / ".env.example").read_text(), encoding="utf-8")
    source = env_file.read_text(encoding="utf-8-sig")
    values = dict(line.split("=", 1) for line in source.splitlines() if "=" in line and not line.startswith("#"))
    for key in ("POSTGRES_PASSWORD", "RABBITMQ_PASSWORD", "MEDIA_ACCESS_KEY", "MEDIA_SECRET_KEY", "PGADMIN_PASSWORD"):
        if not values.get(key):
            values[key] = secrets.token_hex(24)
            source = re.sub(r"^" + key + r"=.*$", key + "=" + values[key], source, flags=re.MULTILINE) if re.search(r"^" + key + "=", source, re.MULTILINE) else source + "\n" + key + "=" + values[key] + "\n"
    env_file.write_text(source, encoding="utf-8")
    values["PGADMIN_EMAIL"] = values.get("PGADMIN_EMAIL") or "pgadmin@example.com"
    values["RABBITMQ_USER"] = values.get("RABBITMQ_USER") or "cityportal"
    env = dict(os.environ, **values)
    env["PUBLIC_ORIGIN"] = "https://localhost:8443"
    command(COMPOSE + ["config", "--quiet"], env)
    if not args.no_build:
        command(COMPOSE + ["build"], env)
    command(COMPOSE + ["up", "-d", "--wait", "postgres", "rabbitmq", "mailpit", "media-store", "pgadmin"], env)
    vhosts = command(COMPOSE + ["exec", "-T", "rabbitmq", "rabbitmqctl", "-q", "list_vhosts", "name"], env, capture=True)
    if "cityportal_stand" not in vhosts.splitlines():
        command(COMPOSE + ["exec", "-T", "rabbitmq", "rabbitmqctl", "add_vhost", "cityportal_stand"], env)
    command(COMPOSE + ["exec", "-T", "rabbitmq", "rabbitmqctl", "set_permissions", "-p", "cityportal_stand", values.get("RABBITMQ_USER", "cityportal"), ".*", ".*", ".*"], env)
    existing = command(COMPOSE + ["exec", "-T", "postgres", "psql", "-X", "-qAt", "-U", "cityportal", "-d", "cityportal"], env,
                       data="SELECT count(*) FROM pg_database WHERE datname='cityportal_stand';", capture=True)
    if existing == "0":
        command(COMPOSE + ["exec", "-T", "postgres", "createdb", "-U", "cityportal", "cityportal_stand"], env)
    command(COMPOSE + ["stop", "api", "worker"], env)
    command(COMPOSE + ["run", "--rm", "--no-deps", "api", "--migrate"], env)
    command(COMPOSE + ["up", "-d", "api", "worker", "frontend", "proxy"], env)
    ca = LOCAL / "root.crt"
    def copy_ca():
        command(COMPOSE + ["cp", "proxy:/data/caddy/pki/authorities/local/root.crt", str(ca)], env, capture=True)
        return ca.is_file()
    wait_for(copy_ca, "local HTTPS certificate")
    client = Client("https://localhost:8443", ca)
    wait_for(lambda: client.call("/api/installation/status"), "HTTPS API")
    accounts_file = LOCAL / "accounts.json"
    if accounts_file.exists():
        accounts = json.loads(accounts_file.read_text())
    else:
        accounts = {role: {"email": "stand-" + role.lower() + "@cityportal.test", "password": secrets.token_hex(10) + "!Aa"} for role in ROLES}
        accounts_file.write_text(json.dumps(accounts, indent=2), encoding="utf-8")
    administrator = accounts["Administrator"]
    if not client.call("/api/installation/status")["installed"]:
        client.call("/api/installation/complete", {**administrator, "displayName": "StandAdministrator", "name": "CityPortal Stand", "city": "Demo City", "region": "", "timeZone": values.get("CITY_TIME_ZONE", "UTC"), "smtpHost": "mailpit", "smtpPort": 1025, "senderAddress": "noreply@cityportal.test", "useTls": False})
    client.call("/api/auth/login", {**administrator, "rememberMe": False})
    # Seed only empty local demo dictionaries; preserve editorial changes on restart.
    for kind, names in {
        "categories": ["Город", "Транспорт", "Культура", "Спорт"],
        "tags": ["Благоустройство", "Дороги", "Образование", "События"],
    }.items():
        if int(client.call("/api/editorial/" + kind)["total"]) == 0:
            for name in names:
                client.call("/api/editorial/" + kind, {"name": name, "archived": False})
    for role in ROLES:
        account = accounts[role]
        person = Client(client.origin, ca)
        if role != "Administrator":
            # Only create missing fixture accounts; never reset somebody's password.
            result = client.call("/api/admin/accounts?query=" + urllib.parse.quote(account["email"]))
            if not any(item["email"].lower() == account["email"] for item in result["items"]):
                person.call("/api/auth/register", {**account, "displayName": "Stand" + role})
        person.call("/api/auth/login", {**account, "rememberMe": False})
        profile = person.call("/api/account")
        if not profile["emailConfirmed"]:
            link = wait_for(lambda: confirmation(account["email"]), "confirmation Email for " + role)
            person.call("/api/auth/confirm-email", link)
        summary = client.call("/api/admin/accounts/" + profile["id"])
        expected = {"RegularUser", role}
        if set(summary["roles"]) != expected:
            client.call("/api/admin/accounts/" + profile["id"] + "/roles", {"version": summary["version"], "roles": sorted(expected)}, method="PUT")
        actual = person.call("/api/account")
        if not actual["emailConfirmed"] or set(actual["roles"]) != expected:
            raise RuntimeError("Unexpected fixture roles for " + role)
    print("\nPortal: https://localhost:8443\npgAdmin: http://localhost:5050\nMailpit: http://localhost:8025", flush=True)
    print("In pgAdmin select Databases > cityportal_stand > Schemas > identity > Tables > AspNetUsers.")
    print("The cityportal database is a separate retained installation with different accounts.")
    print("\nRole           Email                                   Password")
    for role in ROLES:
        print(f"{role:14} {accounts[role]['email']:39} {accounts[role]['password']}")
    print("\npgAdmin login: " + values.get("PGADMIN_EMAIL", "pgadmin@example.com"))
    print("pgAdmin password: " + values["PGADMIN_PASSWORD"])
    print("PostgreSQL: server=CityPortal local PostgreSQL; database=cityportal_stand; user=cityportal")
    print("PostgreSQL password: " + values["POSTGRES_PASSWORD"])
    print("\nAccount credentials: .local/stand/accounts.json; service credentials: .env", flush=True)


if __name__ == "__main__":
    main()
