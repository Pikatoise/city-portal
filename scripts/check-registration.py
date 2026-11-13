"""Exercise registration, SMTP delivery, confirmation and sessions on local Compose.

Creates one disposable account and retains its captured mail for inspection.
Uses only Python's standard library and verifies HTTPS with the supplied CA.
"""

import argparse
import http.cookiejar
import json
import re
import secrets
import ssl
import time
import urllib.error
import urllib.parse
import urllib.request


def require(condition, message):
    if not condition:
        raise AssertionError(message)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--origin", default="https://localhost:8443")
    parser.add_argument("--mailpit", default="http://localhost:8025")
    parser.add_argument("--ca", required=True)
    parser.add_argument("--shell-only", action="store_true", help="Check public SSR and private browser shells without creating an account.")
    parser.add_argument("--install", action="store_true", help="Initialize an empty disposable test site in local setup mode.")
    parser.add_argument("--smtp-host", default="mailpit", help="SMTP host visible from the API, used only by --install.")
    arguments = parser.parse_args()
    origin = arguments.origin.rstrip("/")
    require(urllib.parse.urlsplit(origin).scheme == "https", "HTTPS origin required.")
    cookies = http.cookiejar.CookieJar()
    client = urllib.request.build_opener(
        urllib.request.HTTPCookieProcessor(cookies),
        urllib.request.HTTPSHandler(context=ssl.create_default_context(cafile=arguments.ca)),
    )

    def request(path, expected=200, body=None, csrf=True, method=None):
        headers = {"Accept": "application/json"}
        if body is not None:
            headers["Content-Type"] = "application/json"
            if csrf:
                headers["X-CSRF-TOKEN"] = request("/api/auth/csrf")["token"]
        data = None if body is None else json.dumps(body).encode("utf-8")
        call = urllib.request.Request(origin + path, data=data, headers=headers, method=method)
        try:
            response = client.open(call, timeout=20)
        except urllib.error.HTTPError as error:
            response = error
        with response:
            require(response.status == expected, f"Unexpected HTTP {response.status} at {path}.")
            if path.startswith("/api/auth/") or path.startswith("/api/account"):
                require("no-store" in response.headers.get("Cache-Control", ""), "Private response can be cached.")
            payload = response.read()
            return json.loads(payload) if payload else None

    def mail_json(path):
        with urllib.request.urlopen(arguments.mailpit.rstrip("/") + path, timeout=10) as response:
            return json.load(response)

    def mail_message(message_id):
        if not isinstance(message_id, str) or re.fullmatch(r"[A-Za-z0-9_-]+", message_id) is None:
            raise AssertionError("Invalid Mailpit message ID.")
        return mail_json("/api/v1/message/" + message_id)

    if arguments.install and not request("/api/installation/status")["installed"]:
        setup_email = f"setup-{secrets.token_hex(8)}@example.test"
        request("/api/installation/complete", expected=204, body={
            "email": setup_email, "displayName": "SetupAdmin", "password": secrets.token_hex(12) + "!",
            "name": "TestPortal", "city": "TestCity", "region": "TestRegion", "timeZone": "UTC",
            "smtpHost": arguments.smtp_host, "smtpPort": 1025, "senderAddress": "noreply@cityportal.test", "useTls": False,
        })
        require(request("/api/installation/status")["installed"], "Installation did not persist.")

    installed = request("/api/installation/status")["installed"]
    for path in ("/staff/accounts", "/staff/email-deliveries", "/"):
        with client.open(origin + path, timeout=20) as response:
            require(response.status == 200, "Application shell unavailable.")
            html = response.read().decode("utf-8")
            if not installed:
                require(response.geturl() == origin + "/setup", "Uninstalled site exposed another page.")
                require("no-store" in response.headers.get("Cache-Control", ""), "Setup response can be cached.")
            elif path == "/":
                require('ng-server-context="ssr"' in html, "Public homepage is not server-rendered.")
            else:
                require("no-store" in response.headers.get("Cache-Control", ""), "Staff shell can be cached.")
                require('ng-server-context="ssr"' not in html, "Staff page incorrectly uses server rendering.")
                require("<app-admin-accounts" not in html and "<app-email-deliveries" not in html,
                        "Staff component entered server-rendered HTML.")
    if arguments.shell_only:
        print("Application page isolation and rendering checks passed.")
        return
    require(installed, "Complete local setup first or use --install for a disposable test site.")

    suffix = secrets.token_hex(8)
    email = f"smoke-{suffix}@example.test"
    password = secrets.token_hex(12) + "!"
    registration = {"email": email, "displayName": "SmokeUser", "password": password}
    request("/api/auth/register", expected=400, body=registration, csrf=False)
    account = request("/api/auth/register", expected=201, body=registration)
    require(account["email"] == email and not account["emailConfirmed"], "Unconfirmed account not returned.")
    require("RegularUser" in account["roles"], "Initial regular-user role missing.")
    request("/api/account", expected=401)
    request("/api/auth/login", body={"email": email.upper(), "password": password, "rememberMe": False})
    session_cookie = next((cookie for cookie in cookies if cookie.name == "__Host-CityPortal.Session"), None)
    require(session_cookie is not None and session_cookie.secure, "Secure session cookie missing.")
    require(session_cookie.has_nonstandard_attr("httponly") or session_cookie.has_nonstandard_attr("HttpOnly"), "Session cookie is not HttpOnly.")
    require(session_cookie.discard, "Normal login incorrectly creates a persistent cookie.")
    profile = request("/api/account")
    require(profile["id"] == account["id"] and not profile["emailConfirmed"], "Profile/account mismatch.")

    deadline = time.monotonic() + 90
    message = None
    while time.monotonic() < deadline:
        messages = mail_json("/api/v1/messages?limit=100")["messages"]
        for summary in messages:
            if any(address["Address"].lower() == email for address in summary["To"]):
                message = mail_message(summary["ID"])
                break
        if message is not None:
            break
        time.sleep(1)
    require(message is not None, "Confirmation Email did not reach Mailpit within 90 seconds.")
    require(message["Text"] and message["HTML"], "Plain-text and HTML templates required.")
    links = re.findall(r"https://[^\s<>\"]+", message["Text"])
    confirmation = next((url for url in links if "/account/confirm-email" in url), None)
    require(confirmation is not None, "Confirmation link missing from Email.")
    parsed = urllib.parse.urlsplit(confirmation)
    require(f"{parsed.scheme}://{parsed.netloc}" == origin, "Confirmation link has an unexpected origin.")
    query = urllib.parse.parse_qs(parsed.query)
    # Some link representations use a fragment so secrets never reach proxy request logs.
    if not query:
        query = urllib.parse.parse_qs(parsed.fragment)
    link = {"linkId": query["linkId"][0], "token": query["token"][0]}
    with client.open(origin + parsed.path, timeout=20) as response:
        require(response.status == 200, "Confirmation page unavailable.")
        require(response.headers.get("Referrer-Policy") == "no-referrer", "Confirmation page referrer protection missing.")
    require(not request("/api/account")["emailConfirmed"], "GET confirmation page changed account state.")
    deadline = time.monotonic() + 10
    while time.monotonic() < deadline:
        delivery = request("/api/account/email-confirmation")
        if delivery["state"] == "Sent":
            break
        time.sleep(0.25)
    require(delivery["state"] == "Sent", "SMTP acceptance not reflected in latest delivery status.")
    require(request("/api/auth/confirm-email", body=link)["status"] == "Confirmed", "Confirmation failed.")
    require(request("/api/auth/confirm-email", body=link)["status"] == "AlreadyConfirmed", "Consumed link result incorrect.")
    require(request("/api/account")["emailConfirmed"], "Profile confirmation flag not updated.")
    profile = request("/api/account")
    edited = request("/api/account/display-name", body={"displayName": "Resident2", "version": profile["version"]}, method="PATCH")
    require(edited["displayName"] == "Resident2" and edited["version"] > profile["version"], "Profile edit/version failed.")
    request("/api/account/display-name", expected=409, body={"displayName": "Stale", "version": profile["version"]}, method="PATCH")
    request("/api/auth/forgot-password", expected=202, body={"email": email})
    deadline = time.monotonic() + 30
    recovery = None
    while time.monotonic() < deadline:
        for summary in mail_json("/api/v1/messages?limit=100")["messages"]:
            if any(address["Address"].lower() == email for address in summary["To"]):
                captured = mail_message(summary["ID"])
                recovery = next((url for url in re.findall(r"https://[^\s<>\"]+", captured["Text"]) if "/account/reset-password" in url), None)
                if recovery:
                    require(captured["HTML"] and "1 час" in captured["Text"], "Recovery multipart template/lifetime missing.")
                    break
        if recovery:
            break
        time.sleep(0.5)
    require(recovery is not None, "Recovery Email did not arrive.")
    parsed_recovery = urllib.parse.urlsplit(recovery)
    recovery_parameters = urllib.parse.parse_qs(parsed_recovery.fragment)
    reset = {"linkId": recovery_parameters["linkId"][0], "token": recovery_parameters["token"][0], "newPassword": secrets.token_hex(12) + "!"}
    with client.open(origin + parsed_recovery.path, timeout=20) as response:
        require(response.status == 200, "Recovery page unavailable.")
    require(request("/api/account")["id"] == account["id"], "GET recovery unexpectedly revoked a session.")
    request("/api/auth/reset-password", expected=204, body=reset)
    request("/api/account", expected=401)
    request("/api/auth/reset-password", expected=400, body=reset)
    request("/api/auth/login", expected=401, body={"email": email, "password": password, "rememberMe": False})
    request("/api/auth/login", body={"email": email, "password": reset["newPassword"], "rememberMe": False})
    profile = request("/api/account")
    password = secrets.token_hex(12) + "!"
    request("/api/account/change-password", expected=204, body={"currentPassword": reset["newPassword"], "newPassword": password, "version": profile["version"]})
    request("/api/account", expected=401)
    request("/api/auth/login", body={"email": email, "password": password, "rememberMe": False})
    request("/api/auth/logout", expected=204, body={})
    request("/api/account", expected=401)
    print("HTTPS registration, confirmation, recovery, profile edits, password changes, session revocation and logout passed.")


if __name__ == "__main__":
    main()
