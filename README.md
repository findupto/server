# FindUpTo POS

FindUpTo POS is a local-first restaurant point-of-sale platform. A Windows PC hosts the ASP.NET Core API, SQLite database and local device integrations; Android/Windows clients connect through authenticated HTTP APIs and SignalR realtime events.

## What is implemented

### Server
- ASP.NET Core 8 API
- SQLite + Entity Framework Core
- JWT authentication and server-side RBAC
- Owner, Manager, Admin, Counter, Waiter, Kitchen, Rider and Customer roles
- Business settings
- Categories, products, product barcodes and customer profiles
- Staff and customer orders
- Tax calculation from business settings
- Cash/card/online payment workflow and cash change calculation
- Payment history and realtime payment events
- Kitchen, waiter and rider workflows
- Tables with Available/Occupied/Reserved states
- Promotions and discounts
- Staff/customer messaging, read receipts and typing events
- SignalR realtime order/message/payment/promotion events
- Reports and product/sales summaries
- Audit log endpoints for privileged operations
- Cash drawer sessions, cash-in/out and close variance
- SQLite backup/restore and automatic backup service
- Offline order synchronization with idempotent client operation IDs and conflict records
- Network printer discovery and raw ESC/POS 80mm receipt printing
- Windows printer-agent integration foundation for OS-managed USB/Bluetooth/network printers
- Relay-device authentication and outbound SignalR relay messaging
- Push-device registration/inbox and notification queue foundation
- Windows service hosting support
- Swagger/OpenAPI in development
- Health endpoint
- Login rate limiting

### Flutter
- Customer catalog, promotions, checkout and order history
- Staff login and role-oriented workflows
- Counter POS with barcode search
- Dine-in/table selection
- Cash tender/change validation
- Tax display from server settings
- Offline sale/payment queue and automatic synchronization
- Automatic receipt printing after successful sale, without making printing a prerequisite for recording the sale
- Receipt reprint action
- Kitchen, waiter and rider workflow foundations
- Product management
- Secure JWT storage
- SignalR client integration
- Windows desktop Flutter target

### Build/deployment
- Windows self-contained server publish script
- Windows service installation script
- `BUILD.bat` for restore/build/test/server publish/Flutter APK/Flutter Windows packaging
- Detailed mobile/desktop connection guide in `docs/SETUP.md`

## Important production gaps

The repository contains foundations for several advanced integrations, but these should not be described as complete until the corresponding real external service/hardware is configured and tested:

- Customer identity is currently based on the customer-session flow; phone-number possession is not independently verified. Do not treat it as a strong identity system for sensitive customer accounts.
- Push notifications currently provide device registration and server-side notification queueing; FCM/APNs delivery credentials and production delivery workers must be configured before claiming end-to-end push delivery.
- Voice/WebRTC signaling exists, but full media transport, TURN infrastructure and production call lifecycle are not a finished feature.
- Windows printer-agent A4 rendering and authenticated local-agent routing require final hardware/driver validation on the target Windows machines.
- Remote relay is designed for outbound connectivity but should be deployed behind appropriate network policy and monitoring rather than exposing the POS API directly to the public internet.
- SQLite restore should be performed while POS activity is stopped and followed by a restart/verification.

These are explicit engineering boundaries, not fake feature claims.

## Architecture

```text
 Android / Windows clients
          |
       HTTP + JWT
       SignalR WS
          |
 +--------+------------------+
 | Windows POS Server        |
 | ASP.NET Core 8            |
 | RBAC / Orders / Payments  |
 | Reports / Audit / Sync    |
 | Printer / Relay services  |
 +------------+--------------+
              |
           SQLite
              |
       local backups

 Local Windows devices:
   USB/Bluetooth/LAN printers
   barcode scanners
   cash drawer hardware
```

Clients never access SQLite directly. The server validates every protected operation and remains the source of truth when online. Offline client queues are replayed through server validation and idempotency checks.

## Repository layout

```text
server/
├── src/FindUpTo.Pos.Server/       # ASP.NET Core API
├── mobile/flutter_app/             # Flutter Android/Windows client
├── tools/FindUpTo.PrinterAgent/    # Windows local printer integration
├── deploy/windows/                 # Windows publish/service scripts
├── docs/                            # Architecture and setup documentation
├── tests/                           # Server tests when present
├── BUILD.bat                        # One-command Windows build/package
└── README.md
```

## Build everything on Windows

Requirements:
- .NET 8 SDK
- Flutter SDK
- Android SDK for APK builds
- Visual Studio 2022 with Desktop development with C++ for Flutter Windows builds

Set production/deployment secrets in the shell first:

```bat
set POS_JWT_KEY=replace-with-a-random-secret-at-least-32-characters
set INITIAL_OWNER_PASSWORD=strong-owner-password
set INITIAL_ADMIN_PASSWORD=strong-admin-password
set INITIAL_WAITER_PASSWORD=strong-waiter-password
set INITIAL_COUNTER_PASSWORD=strong-counter-password
```

Optional first-run business settings:

```bat
set POS_BUSINESS_NAME=Your Restaurant
set POS_BUSINESS_PHONE=03001234567
set POS_BUSINESS_ADDRESS=Your address
set POS_TAX_PERCENT=0
set POS_CURRENCY_CODE=PKR
set POS_CURRENCY_SYMBOL=Rs.
```

Then:

```bat
BUILD.bat
```

Outputs are written to `artifacts/windows-server/`, `mobile/flutter_app/build/app/outputs/flutter-apk/`, and `mobile/flutter_app/build/windows/x64/runner/Release/`.

For detailed installation, LAN setup, Android connection, Windows connection, printing, offline sync and troubleshooting, read `docs/SETUP.md`.

## Development

Run the server:

```bat
dotnet restore
dotnet run --project src/FindUpTo.Pos.Server
```

Run Flutter:

```bat
cd mobile/flutter_app
flutter pub get
flutter run --dart-define=POS_SERVER_URL=http://127.0.0.1:5000
```

For an Android emulator use `http://10.0.2.2:5000`. For a physical phone use the Windows PC's LAN address, such as `http://192.168.1.50:5000`.

## Security rules

1. Never commit JWT signing keys, passwords, API secrets or printer-agent secrets.
2. `POS_JWT_KEY` must be at least 32 characters; use a randomly generated secret in production.
3. Initial staff passwords are read from environment variables and are never seeded from hardcoded source passwords.
4. Mobile/desktop clients never connect directly to SQLite.
5. Server-side authorization is mandatory; hiding a UI button is not a permission boundary.
6. Do not expose the POS API or SQLite database directly to the public internet.
7. Restrict Windows Firewall access to the private LAN where possible.
8. Treat backups as sensitive business data and protect them accordingly.
9. Test restore procedures on a separate machine before relying on them for disaster recovery.

## Reference projects researched for architecture ideas

The implementation direction follows patterns seen in mature/open-source POS projects: local-first operation, queued offline actions, printer abstraction, role-based workflows, auditability, backups and feature-first client structure. Useful references include Flutter POS examples with offline queues and printer support, and offline restaurant POS projects with SQLite, KDS and operational tooling. urlFlutter POS reference projecthttps://github.com/elrizwiraswara/flutter_pos urlFloCafe reference projecthttps://github.com/FreeOpenSourcePOS/FloCafe urlBayaa POS reference projecthttps://github.com/Desha29/Bayaa

## License

Project licensing is controlled by the repository owner and should be defined before production distribution.
