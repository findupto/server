# FindUpTo POS Server

A local-first restaurant POS platform designed around a Windows PC that acts as the primary application server and database host, with Android/Windows clients connected through secure APIs and real-time events.

## Current implementation status

The repository is beyond the original Phase 1 foundation. The following functionality is currently implemented in the server and Flutter client code.

### Server and data
- [x] ASP.NET Core 8 server
- [x] SQLite + Entity Framework Core
- [x] Business settings read/update
- [x] Seeded staff users and role-based access
- [x] Password hashing
- [x] JWT authentication and `/api/me`
- [x] Server-side role authorization
- [x] Swagger/OpenAPI in development
- [x] Health endpoint

### Products, customers and orders
- [x] Categories: list/create
- [x] Products: list/create/update
- [x] Customer profiles: list/search/create
- [x] Staff order creation
- [x] Order list/detail/status updates
- [x] Order item pricing and subtotal calculation
- [x] Tax calculation from business settings
- [x] Delivery and pickup order types
- [x] Customer order history and order detail

### POS workflow
- [x] Counter order screen
- [x] Cash payment collection
- [x] Cash change calculation
- [x] Payment history
- [x] Kitchen order queue
- [x] Kitchen status flow: New → Accepted → Preparing → Ready
- [x] Waiter order queue
- [x] Waiter serving/completion action
- [x] Rider delivery queue
- [x] Rider status flow: Ready → OutForDelivery → Completed
- [x] Role-specific workflow authorization

### Promotions and communication
- [x] Customer-visible active promotions
- [x] Staff promotion CRUD
- [x] Percent and fixed discounts
- [x] Product/category promotion targeting
- [x] Staff/customer conversations
- [x] Text messages
- [x] Read receipts
- [x] Typing events
- [x] SignalR realtime order/message/payment/promotion events
- [x] SignalR user/conversation groups
- [x] Call signaling foundation
- [ ] Voice-message media storage
- [ ] Full WebRTC call/media implementation
- [ ] Push notifications

### Flutter client
- [x] Customer app foundation
- [x] Customer catalog
- [x] In-memory cart
- [x] Customer checkout
- [x] Staff login/API integration
- [x] Counter orders/payment screen
- [x] Kitchen dashboard
- [x] Waiter dashboard
- [x] Rider dashboard
- [x] Product management screen
- [x] Secure JWT token storage
- [x] SignalR client integration
- [ ] Complete production-ready role-specific UX for every workflow
- [ ] Windows desktop POS application

### Production features still missing
- [ ] Tables/table management
- [ ] Receipt printing
- [ ] Barcode support
- [ ] Cash drawer integration
- [ ] Reports and analytics
- [ ] Audit logs
- [ ] Bulk product import/export
- [ ] Automatic backup/restore
- [ ] Offline transaction queue and synchronization
- [ ] Secure remote relay/outbound connectivity
- [ ] Windows installer
- [ ] Windows service packaging
- [ ] Automated test suite
- [ ] End-to-end/integration test coverage

## Initial Business Configuration

These values are seed defaults and are editable later from Owner/Manager settings:

- Business: **MK Pizza & Ice Bar**
- Phone: **03169700025**
- Address: **Abbas Chowk Collage Road Bhakkar**
- Tax: **0%**
- Currency: **PKR (Rs.)**

Default seeded usernames are `Malik` (Owner), `MK` (Admin), `WR` (Waiter), and `CP` (Counter Person). Initial passwords are supplied through secure deployment configuration rather than committed to source control. If the required environment variables are absent, the current development seed falls back to `CHANGE_ME_<username>` and should not be used in production.

## Architecture

```text
Customer / Waiter / Rider / Kitchen / Counter / Manager / Owner
                         |
                  HTTPS + WebSocket
                         |
                 Windows POS Server
              +----------+-----------+
              | ASP.NET Core API     |
              | SignalR realtime     |
              | JWT authentication   |
              | Role permissions     |
              | Order workflow       |
              | Messaging            |
              +----------+-----------+
                         |
                      SQLite
```

The current repository contains the server and Flutter client foundations. Windows service/desktop packaging and the production remote-access layer are planned but are not yet implemented.

## Repository Layout

```text
server/
├── src/
│   └── FindUpTo.Pos.Server/
│       ├── Data/
│       ├── Endpoints/
│       ├── Hubs/
│       ├── Models/
│       ├── Services/
│       ├── Program.cs
│       └── FindUpTo.Pos.Server.csproj
├── mobile/
│   └── flutter_app/
├── docs/
├── .github/workflows/
└── README.md
```

## Development

Requirements: .NET 8 SDK.

```bash
dotnet restore
dotnet run --project src/FindUpTo.Pos.Server
```

The API uses SQLite storage and seeds the initial business settings and users on first run.

The Flutter client is under `mobile/flutter_app`. Its API base URL defaults to `http://10.0.2.2:5000` for an Android emulator and can be overridden with `POS_SERVER_URL`.

## Security Notes

1. Mobile and desktop clients never access SQLite directly.
2. Protected operations are authorized on the server.
3. Roles are not trusted merely because a client hides a button.
4. Production passwords must be changed from development/seed values.
5. Production JWT signing keys must be supplied through configuration/environment; never rely on the development fallback key.
6. Remote access, backups, audit logging and offline synchronization are not yet production-complete.

## Roadmap

### Completed foundation
- [x] Server, SQLite and EF Core
- [x] Authentication and role authorization
- [x] Categories, products and customers
- [x] Orders and order status workflow
- [x] Payments
- [x] Promotions
- [x] Kitchen/waiter/rider workflows
- [x] Customer ordering API and Flutter foundation
- [x] Staff Flutter workflow foundation
- [x] SignalR realtime and text messaging foundation

### Next production priorities
1. Add automated unit/integration tests and run them in CI.
2. Implement audit logs for privileged changes and payments.
3. Implement backups and restore verification.
4. Implement offline-first transaction queuing and synchronization.
5. Complete tables, receipts, barcode and cash-drawer support.
6. Harden customer authentication and production JWT configuration.
7. Complete WebRTC/media and push notification infrastructure.
8. Package the server as a Windows service and deliver the production desktop POS client.
9. Add secure remote connectivity without exposing SQLite directly.

## License

Project licensing will be defined by the repository owner before production distribution.
