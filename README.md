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
- SignalR realtime order/message/payment/delivery events
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
- **AI Business Operator with live POS tool execution**
- **Automatic free/local AI discovery: Ollama, LM Studio and llama.cpp**
- **Owner AI configuration for purchased API-key/model providers**
- **Rider GPS location, availability, assignment and live management map**
- **Public parcel tracking code with live SignalR location updates**

### AI Business Operator
Owner, Manager, Admin and Counter users can open the AI Operator from the staff dashboard and give natural-language commands. The AI uses authenticated server-side tools rather than pretending to perform actions.

Supported live operations include:
- Search products and read live stock/cost information
- Create sales and optionally collect Cash/Card/Online payments
- Automatically deduct tracked inventory during AI-created sales
- Add stock or set exact stock quantities
- Update product price, availability and barcode
- Read low-stock alerts and sales/payment reports
- Create purchase orders from existing suppliers
- Receive purchase orders and update inventory weighted average cost
- Return printable receipt URLs for completed sales
- Role-aware authorization and audit entries for AI mutations

AI mode is **automatic by default**. The server first checks free local AI servers (Ollama, LM Studio, llama.cpp). No API key is required when one of those local servers is available. The Owner can select a purchased model/provider, model name, API key and optional base URL from **AI Configuration**. Purchased API keys are protected at rest and never returned to clients.

For OpenAI, the server uses the Responses API and function calling. urlOpenAI Responses API documentationhttps://platform.openai.com/docs/api-reference/responses

### Rider GPS and parcel tracking
- Rider accounts use the existing `Rider` role.
- Owner/Manager/Admin can create rider profiles from existing staff users.
- Riders can switch availability on/off and share GPS coordinates periodically.
- Owner/Manager/Admin/Counter can see live rider locations on a map.
- Delivery orders can receive a unique tracking code and a rider assignment.
- Customers can track a delivery using the tracking code without exposing staff APIs.
- Customer apps can display the rider and destination on an OpenStreetMap-based map and receive live location updates through SignalR.
- Routing/map foundations use free OpenStreetMap tiles; routing can be extended with self-hosted OSRM when turn-by-turn routing is required. OSRM supports fast routing and map matching. citeturn0search2turn0search9

### Flutter
- Customer catalog, promotions, checkout and order history
- Pickup or Delivery checkout
- Customer parcel tracking map with live rider position
- Staff login and role-oriented workflows
- Counter POS with barcode search
- Dine-in/table selection
- Cash tender/change validation
- Tax display from server settings
- Offline sale/payment queue and automatic synchronization
- Automatic receipt printing after successful sale, without making printing a prerequisite for recording the sale
- Receipt reprint action
- Kitchen, waiter and rider workflow foundations
- Rider GPS sharing screen
- Management live rider map
- Product management
- **AI Operator screen for Owner/Manager/Admin/Counter**
- **Owner AI Configuration screen**
- Secure JWT storage
- SignalR client integration
- Windows desktop Flutter target

## AI configuration

### Automatic free AI
Set nothing. The server automatically checks local:
- Ollama: `127.0.0.1:11434`
- LM Studio: `127.0.0.1:1234`
- llama.cpp server: `127.0.0.1:8080`

If none is available, AI remains disabled until the Owner configures a purchased provider/model or the deployment supplies `OPENAI_API_KEY`.

### Purchased model
Use **Staff Dashboard → AI Configuration** as Owner. Select the provider, model, optional base URL and API key. The API key is stored protected at rest and is never sent to Flutter clients.

Environment variables remain supported for deployment automation:

```bat
set OPENAI_API_KEY=your-server-side-openai-api-key
set OPENAI_MODEL=gpt-5
```

AI endpoints:

```text
POST /api/ai/operate
GET  /api/ai/status
GET  /api/ai/providers/discover
PUT  /api/ai/configuration
```

Example command:

```text
Sell 2 Zinger Burger and 1 Coke for cash. Customer paid Rs. 1000.
```

The assistant resolves the products, creates the real POS order, deducts tracked stock, records payment/change, audits the operation and returns the receipt URL.

## Delivery tracking endpoints

```text
POST /api/delivery/riders
GET  /api/delivery/riders
POST /api/delivery/riders/me/availability
POST /api/delivery/riders/me/location
POST /api/delivery/orders/{orderId}/assign-rider
POST /api/delivery/orders/{orderId}/tracking
GET  /api/customer/orders/{orderId}/tracking
GET  /api/delivery/track/{trackingCode}
```

## Important production boundaries

- Background GPS sharing requires the target Android/Windows deployment to grant the operating system location permission and, for background tracking, the appropriate platform background-location configuration.
- OpenStreetMap tiles are free to use subject to their usage policy; high-volume production deployments should use an appropriate tile provider or self-hosted tiles.
- Turn-by-turn routing is not the same as map display; use a self-hosted OSRM/Valhalla service if the business needs route optimization, ETA and navigation.
- Customer identity is currently based on the customer-session flow; phone-number possession is not independently verified.
- Push notifications currently provide device registration and server-side notification queueing; FCM/APNs delivery credentials and production delivery workers must be configured before claiming end-to-end push delivery.
- Voice/WebRTC signaling exists, but full media transport, TURN infrastructure and production call lifecycle are not a finished feature.
- Windows printer-agent A4 rendering and authenticated local-agent routing require final hardware/driver validation on target Windows machines.
- SQLite restore should be performed while POS activity is stopped and followed by restart/verification.
- AI financial actions remain protected by server-side role checks. Autonomous actions should be governed by the Owner's operational policy.

These are explicit engineering boundaries, not fake feature claims.

## Architecture

```text
 Android / Windows clients
          |
       HTTP + JWT
       SignalR WS
          |
 +--------+---------------------------+
 | Windows POS Server                 |
 | ASP.NET Core 8                     |
 | RBAC / Orders / Payments           |
 | Reports / Audit / Sync             |
 | AI Operator / Tool Execution       |
 | Rider GPS / Delivery Tracking      |
 | Printer / Relay services           |
 +----------------+------------------+
                  |
               SQLite
                  |
            local backups

 Free/local AI:
   Ollama / LM Studio / llama.cpp

 Local Windows devices:
   USB/Bluetooth/LAN printers
   barcode scanners
   cash drawer hardware
```

Clients never access SQLite directly. The server validates every protected operation and remains the source of truth when online. Offline client queues are replayed through server validation and idempotency checks.

## Repository layout

```text
server/
├── src/FindUpTo.Pos.Server/       # ASP.NET Core API + AI + delivery tracking
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

Then:

```bat
BUILD.bat
```

For detailed installation, LAN setup, Android connection, Windows connection, printing, offline sync and troubleshooting, read `docs/SETUP.md`.

## Security rules

1. Never commit JWT signing keys, passwords, API secrets or printer-agent secrets.
2. `POS_JWT_KEY` must be at least 32 characters; use a randomly generated secret in production.
3. AI API keys are server-side secrets and must never be shipped to Flutter clients.
4. Initial staff passwords are read from environment variables and are never seeded from hardcoded source passwords.
5. Mobile/desktop clients never connect directly to SQLite.
6. Server-side authorization is mandatory; hiding a UI button is not a permission boundary.
7. AI tool execution is subject to the authenticated user's server role.
8. Do not expose the POS API or SQLite database directly to the public internet.
9. Restrict Windows Firewall access to the private LAN where possible.
10. Treat backups as sensitive business data and protect them accordingly.
11. Test restore procedures on a separate machine before relying on them for disaster recovery.

## Reference projects researched for architecture ideas

The implementation direction follows patterns seen in mature/open-source POS projects: local-first operation, queued offline actions, printer abstraction, role-based workflows, auditability, backups, KDS-oriented operations and feature-first client structure. FloCafe emphasizes offline-first restaurant operation, printing, KDS and local SQLite; DearPOS emphasizes self-hosted/offline workflows; Kitchen-POS demonstrates thermal printing, inventory and table/order workflows. citeturn0search0turn0search1turn0search7

## License

Project licensing is controlled by the repository owner and should be defined before production distribution.