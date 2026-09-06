# FindUpTo POS Runbook

## What is implemented

The repository contains a working ASP.NET Core POS server plus Flutter Android/Windows clients. Core areas implemented in the codebase include authentication/RBAC, catalog, orders, payments, inventory, purchasing, suppliers, promotions, customers/credit, tables, reports, finance/audit, cash drawer, offline sync/idempotency, SignalR realtime, rider/delivery tracking, OSRM routing when configured, AI tool execution, network ESC/POS printing, and the Windows printer agent.

## Features that still depend on external setup

- Native FCM/APNs push delivery requires a provider adapter/configuration; realtime notification delivery through SignalR is implemented.
- A4/document printing is not the same as the implemented RAW/ESC-POS receipt path.
- WebRTC has signaling support, but a production media/TURN deployment is still required for internet calling.
- Customer phone ownership verification is not implemented; do not treat a phone number alone as verified identity.
- AI features require a configured provider or a locally installed compatible model.
- OSRM delivery routing requires `POS_OSRM_URL`.

## Windows build

Run from the repository root on Windows with .NET SDK, Flutter SDK, and Android/Windows Flutter prerequisites installed.

```bat
set POS_JWT_KEY=REPLACE_WITH_A_LONG_RANDOM_SECRET
set INITIAL_OWNER_PASSWORD=REPLACE_WITH_OWNER_PASSWORD
set INITIAL_ADMIN_PASSWORD=REPLACE_WITH_ADMIN_PASSWORD
set INITIAL_WAITER_PASSWORD=REPLACE_WITH_WAITER_PASSWORD
set INITIAL_COUNTER_PASSWORD=REPLACE_WITH_COUNTER_PASSWORD
set POS_BUSINESS_NAME=Your Restaurant
set POS_BUSINESS_PHONE=Your Phone
set POS_BUSINESS_ADDRESS=Your Address
set POS_TAX_PERCENT=0
set POS_CURRENCY_CODE=PKR
set POS_CURRENCY_SYMBOL=Rs.
BUILD.bat
```

Build outputs:

- `artifacts\windows-server\FindUpTo.Pos.Server.exe`
- `mobile\flutter_app\build\app\outputs\flutter-apk\app-release.apk`
- `mobile\flutter_app\build\windows\x64\runner\Release\`

## Start the server

Keep the required environment variables in the same Windows shell and run:

```bat
START_SERVER.bat
```

The launcher defaults to port 5000 and now actually executes the published server executable.

For development, run the server project directly with `dotnet run` from the repository root.

## First login

The database initializer creates the configured initial staff accounts. The usernames are:

- Malik — Owner
- MK — Admin
- WR — Waiter
- CP — Counter

Their passwords come from the corresponding `INITIAL_*_PASSWORD` environment variables. Change/rotate these before production use.

## Data

The code intentionally does not invent a real restaurant's menu, prices, suppliers, tax rules, or business records. Configure business settings and create the actual catalog/tables/staff through the application or API. Demo data should be treated as test data only.

## Android on a LAN

Build/run the Flutter app with `POS_SERVER_URL` pointing at the Windows server's LAN address and port. The phone and server PC must be able to reach each other, and Windows Firewall must allow the selected port.

Example shape:

```text
flutter run --dart-define=POS_SERVER_URL=<SERVER_LAN_ADDRESS_AND_PORT>
flutter build apk --release --dart-define=POS_SERVER_URL=<SERVER_LAN_ADDRESS_AND_PORT>
```

## Health check

After startup, verify the server's `/health` endpoint. It should report service status `ok`.

## Production checklist

1. Use a long random `POS_JWT_KEY` and keep it out of source control.
2. Set strong initial passwords before the first production startup.
3. Configure the real business identity, currency, tax, printers, and catalog.
4. Configure database/backup storage appropriate for the deployment.
5. Configure OSRM if delivery routing is required.
6. Configure an FCM/APNs adapter if native mobile push is required.
7. Deploy TURN/media infrastructure if internet WebRTC calling is required.
8. Test Windows printer connectivity with the actual ESC/POS printer before opening the store.
9. Test offline sync and backup/restore before production use.
