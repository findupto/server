# FindUpTo POS Android Client

This directory defines the Android/mobile client contract for the local-first POS server.

## Customer MVP

1. Create customer session with `POST /api/customer/session`.
2. Store the returned JWT in secure device storage.
3. Load menu from `/api/customer/products` and promotions from `/api/customer/promotions`.
4. Maintain the cart locally and submit it to `/api/customer/orders`.
5. Track orders with `/api/customer/orders` and `/api/customer/orders/{id}`.
6. Connect to `/hubs/pos` with the JWT for realtime `order.updated`, `message.created`, `messages.read`, `typing.changed`, and call signaling events.

## Staff MVP

Staff authenticate with `/api/auth/login` and use role-specific endpoints for counter, waiter, kitchen, rider, admin, manager, and owner workflows.

## Recommended Flutter structure

- `lib/core/api/` — HTTP client, auth, retry and server discovery.
- `lib/core/realtime/` — SignalR connection and event routing.
- `lib/features/customer/` — menu, cart, checkout, orders, chat.
- `lib/features/staff/` — role-aware dashboards and workflows.
- `lib/features/settings/` — server URL and local-device configuration.

The PC-hosted server remains authoritative. The Android client never connects directly to SQLite.
