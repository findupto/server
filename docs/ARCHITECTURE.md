# Architecture and Implementation Plan

## Core rule

Clients never connect to SQLite directly. They communicate with the POS server through authenticated HTTP APIs and SignalR realtime events.

## Roles

- Owner: unrestricted business administration.
- Manager: operational administration granted by Owner.
- Admin: configurable administrative access.
- Counter: POS sales, customers, payments and order operations granted by Owner/Manager.
- Waiter: assigned orders, new table orders, serving/completion and staff communication.
- Kitchen: kitchen-visible orders and production status.
- Rider: assigned deliveries, addresses, delivery status and authorized staff/customer contact.
- Customer: public catalog, cart, ordering and authorized counter communication.

## Data ownership

The restaurant Windows PC is the authoritative local data host. The cloud layer, when introduced, is a connectivity/notification/relay layer rather than a direct database endpoint.

## Realtime

SignalR will publish events such as `order.created`, `order.updated`, `order.ready`, `delivery.assigned`, `message.created`, `user.presence`, and communication session events.

## Communications

Text and voice-message metadata are stored on the server. Voice messages are stored as protected media files. Live calls use WebRTC with the server/cloud layer providing authentication and signaling.

## Offline-first behavior

The POS must continue operating during an internet outage. Local transactions are committed first, queued for synchronization, and reconciled when connectivity returns. Conflict rules will be defined per entity before the sync engine is enabled.

## Security

- Passwords are hashed and never committed to source control.
- JWT is used for API authentication in the current foundation.
- Production JWT signing keys must come from environment/secret configuration.
- Server-side role authorization is mandatory.
- Audit logging will cover privileged changes, payments, refunds, product deletion and permission changes.
