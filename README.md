# FindUpTo POS Server

A local-first restaurant POS platform designed around a Windows PC that acts as the primary application server and database host, with Android/Windows clients connected through secure APIs and real-time events.

## Initial Business Configuration

These values are seed defaults and are editable later from Owner/Manager settings:

- Business: **MK Pizza & Ice Bar**
- Phone: **03169700025**
- Address: **Abbas Chowk Collage Road Bhakkar**
- Tax: **0%**
- Currency: **PKR (Rs.)**

Default seeded usernames are `Malik` (Owner), `MK` (Admin), `WR` (Waiter), and `CP` (Counter Person). Initial passwords are supplied through secure deployment configuration rather than committed to source control.

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
              | Authentication       |
              | Role permissions     |
              | Order workflow       |
              | Messaging foundation |
              +----------+-----------+
                         |
                      SQLite
```

The Windows installation will run the server as a Windows service and the POS UI as a desktop client. The Android application will use the same API and realtime contract, with the authenticated role determining the available screens and actions.

## Planned Modules

- Product and category management
- Cart and customer ordering
- POS counter and cash payments
- Tables and waiter orders
- Kitchen order display and order status workflow
- Rider delivery assignment and delivery tracking
- Customer profiles and order history
- Promotions visible to customers
- Staff accounts, roles and permissions
- Realtime order/status notifications
- Staff/customer text messaging
- Voice messages and WebRTC calling foundation
- Reports and audit logs
- Bulk product import/export
- Local database and automatic backups
- Offline-first POS operation with synchronization when connectivity returns
- Secure remote access without exposing the local database directly

## Repository Layout

```text
server/
├── src/
│   └── FindUpTo.Pos.Server/
│       ├── Data/
│       ├── Models/
│       ├── Services/
│       ├── Program.cs
│       └── FindUpTo.Pos.Server.csproj
├── docs/
├── tests/
└── README.md
```

## Development

Requirements: .NET 8 SDK.

```bash
dotnet restore
dotnet run --project src/FindUpTo.Pos.Server
```

The API starts with SQLite storage and seeds the initial business settings and users on first run.

## Security Principles

1. Mobile and desktop clients never access the database directly.
2. Every protected operation is authorized on the server.
3. Roles are not trusted merely because a client hides a button.
4. Production passwords must be changed from the seeded defaults.
5. Remote access will use TLS and a secure outbound connection/relay rather than requiring database exposure.
6. Sensitive operations will be recorded in audit logs.

## Implementation Roadmap

### Phase 1 — Server foundation
- [x] ASP.NET Core server
- [x] SQLite database
- [x] Business settings seed
- [x] Role/user seed
- [x] Password hashing
- [x] Authentication endpoint
- [x] Role-protected API foundation
- [x] SignalR realtime hub foundation
- [ ] Products/categories
- [ ] Orders/cart
- [ ] Payments

### Phase 2 — POS
- [ ] Windows desktop POS UI
- [ ] Receipt printing
- [ ] Barcode support
- [ ] Cash drawer integration
- [ ] Reports

### Phase 3 — Mobile
- [ ] Customer app
- [ ] Waiter app
- [ ] Rider app
- [ ] Kitchen app
- [ ] Admin/Owner app

### Phase 4 — Communications
- [ ] Text chat
- [ ] Voice messages
- [ ] WebRTC calling
- [ ] Push notifications

### Phase 5 — Production
- [ ] Windows installer
- [ ] Windows service packaging
- [ ] Automatic backup/restore
- [ ] Offline synchronization
- [ ] Secure remote relay
- [ ] Automated tests and CI

## License

Project licensing will be defined by the repository owner before production distribution.
