"""Crea un negocio de demostración en la API LOCAL de KipuPOS (solo desarrollo).

Uso:  python tools/seed-demo.py [--api https://localhost:7100]
      python tools/seed-demo.py --tenant tostadores-cafe --name "Tostadores Café" --logos assets/clientes/tostadores-cafe

Sigue el mismo flujo que KipuPOS.http: usuario de plataforma -> negocio -> admin -> catálogo -> menú de hoy -> cajero.
Es idempotente: crea solo lo que falta y vuelve a cargar el menú de hoy.
El catálogo es de ejemplo; reemplázalo por la carta real del negocio desde el panel.
Las contraseñas de abajo son de PRUEBA para la base local; nunca las uses en producción.
"""
import argparse
import base64
import datetime
import json
import pathlib
import ssl
import urllib.error
import urllib.request

ARGS = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
ARGS.add_argument("--api", default="https://localhost:7100")
ARGS.add_argument("--tenant", default="kipu-demo")
ARGS.add_argument("--name", default="Café Demo")
ARGS.add_argument("--branch", default="Sucursal Centro")
ARGS.add_argument("--logos", help="carpeta con logo-*.png (el que termina en -claro.png es para fondos oscuros)")
OPTS = ARGS.parse_args()
BASE = OPTS.api.rstrip("/")

PLATFORM_USER = "platform"
PLATFORM_SEED_PASSWORD = "CambiarAhora#2026"   # Seed:PlatformPassword de appsettings.json
PLATFORM_PASSWORD = "Plataforma#Nueva2026"      # la misma que usa KipuPOS.http tras el cambio obligatorio

TENANT = OPTS.tenant
ADMIN_USER, ADMIN_PASSWORD = "admin", "Admin#Demo2026"
CASHIER_USER, CASHIER_PASSWORD = "caja1", "Caja1#Demo2026"

CATALOG = {
    "Menú del día": [("Ají de gallina", 12, True), ("Lomo saltado", 15, True), ("Arroz con pollo", 11, True),
                     ("Sopa de quinua", 6, True), ("Causa limeña", 7, True)],
    "Bebidas": [("Chicha morada", 3.5, False), ("Café pasado", 4, False), ("Limonada", 4.5, False), ("Agua mineral", 2.5, False)],
    "Postres": [("Mazamorra morada", 4, False), ("Arroz con leche", 4, False), ("Torta de chocolate", 6.5, False)],
    "Extras": [("Táper", 1, False), ("Porción de arroz", 3, False), ("Pan con mantequilla", 2, False)],
}
SOLD_OUT = {"Arroz con pollo"}

# El certificado de desarrollo de ASP.NET puede no estar en el almacén de Python
CTX = ssl._create_unverified_context() if BASE.startswith("https://localhost") else None


def call(method, path, body=None, token=None, ok=(200, 201, 204)):
    req = urllib.request.Request(BASE + path, method=method,
                                 data=None if body is None else json.dumps(body).encode(),
                                 headers={"Content-Type": "application/json"})
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req, context=CTX) as res:
            text = res.read().decode()
            return res.status, (json.loads(text) if text else None)
    except urllib.error.HTTPError as e:
        text = e.read().decode()
        if e.code in ok:
            return e.code, None
        return e.code, text


def login(tenant, user, passwords):
    for pw in passwords:
        status, body = call("POST", "/api/auth/login", {"tenantCode": tenant, "username": user, "password": pw})
        if status == 200:
            return body, pw
    raise SystemExit(f"No se pudo iniciar sesión como {user} ({tenant or 'plataforma'}): {status} {body}")


def ensure_password(session, current, new):
    if not session["mustChangePassword"]:
        return session
    status, body = call("POST", "/api/auth/change-password", {"currentPassword": current, "newPassword": new}, session["token"])
    if status != 200:
        raise SystemExit(f"Cambio de contraseña falló: {status} {body}")
    return body


def main():
    plat, used = login(None, PLATFORM_USER, [PLATFORM_PASSWORD, PLATFORM_SEED_PASSWORD])
    plat = ensure_password(plat, used, PLATFORM_PASSWORD)
    ptoken = plat["token"]

    _, tenants = call("GET", "/api/platform/tenants", token=ptoken)
    if not any(t["code"] == TENANT for t in tenants):
        status, created = call("POST", "/api/platform/tenants", {
            "code": TENANT, "name": OPTS.name, "ruc": None, "branchName": OPTS.branch,
            "adminUsername": ADMIN_USER, "adminFullName": "Administrador Demo"}, ptoken)
        if status != 201:
            raise SystemExit(f"No se pudo crear el negocio: {status} {created}")
        admin = ensure_password(login(TENANT, ADMIN_USER, [created["temporaryPassword"]])[0], created["temporaryPassword"], ADMIN_PASSWORD)
        print(f"Negocio '{TENANT}' creado.")
    else:
        admin = login(TENANT, ADMIN_USER, [ADMIN_PASSWORD])[0]
        print(f"El negocio '{TENANT}' ya existía.")
    atoken = admin["token"]

    # Catálogo: solo se crea lo que falta (por nombre)
    _, cats = call("GET", "/api/categories", token=atoken)
    _, prods = call("GET", "/api/products", token=atoken)
    existing = {p["name"] for p in prods}
    for order, (cat, items) in enumerate(CATALOG.items(), start=1):
        c = next((x for x in cats if x["name"] == cat), None)
        if c is None:
            _, c = call("POST", "/api/categories", {"name": cat, "sortOrder": order, "active": True}, atoken)
        for name, price, is_menu in items:
            if name not in existing:
                call("POST", "/api/products", {"categoryId": c["id"], "name": name, "price": price, "active": True, "isMenuItem": is_menu}, atoken)

    # Cajero en la sucursal del administrador
    _, users = call("GET", "/api/users", token=atoken)
    if not any(u["username"] == CASHIER_USER for u in users):
        status, user = call("POST", "/api/users", {"fullName": "Cajero Demo", "username": CASHIER_USER, "role": "Cajero", "branchId": admin["branchId"]}, atoken)
        if status != 201:
            raise SystemExit(f"No se pudo crear el cajero: {status} {user}")
        ensure_password(login(TENANT, CASHIER_USER, [user["temporaryPassword"]])[0], user["temporaryPassword"], CASHIER_PASSWORD)
        print("Cajero creado.")

    # Logos: "logo" para fondos claros y "logo-dark" (archivo *-claro.png) para fondos oscuros
    if OPTS.logos:
        folder = pathlib.Path(OPTS.logos)
        for kind, pattern in (("logo", "logo-*.png"), ("logo-dark", "logo-*-claro.png")):
            files = [f for f in sorted(folder.glob(pattern)) if kind == "logo-dark" or not f.stem.endswith(("-claro", "-ticket"))]
            if files:
                data = base64.b64encode(files[0].read_bytes()).decode()
                status, body = call("PUT", f"/api/tenant/{kind}", {"dataBase64": data}, atoken)
                print(f"Logo {kind} ({files[0].name}): {status}" + ("" if status == 200 else f" {body}"))

    # Menú de hoy (hora de Lima) en la sucursal del administrador
    _, tenant = call("GET", "/api/tenant", token=atoken)
    today = (datetime.datetime.now(datetime.timezone.utc) + datetime.timedelta(hours=tenant["utcOffsetHours"])).date().isoformat()
    _, products = call("GET", "/api/products?onlyActive=true", token=atoken)
    entries = [{"productId": p["id"], "price": p["price"], "soldOut": p["name"] in SOLD_OUT} for p in products if p["isMenuItem"]]
    status, body = call("PUT", f"/api/menu/{today}", {"entries": entries}, atoken)
    print(f"Menú de {today}: {status}" + ("" if status == 200 else f" {body}"))
    print(f"Listo. Código de negocio: {TENANT} · usuarios: {ADMIN_USER}, {CASHIER_USER} (contraseñas en este archivo).")


if __name__ == "__main__":
    main()
