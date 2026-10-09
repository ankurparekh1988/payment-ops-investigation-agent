# 0003. Server-rendered UI as its own backend, with credential-free sign-in

- **Status:** Accepted
- **Date:** 2026-10-09

## Context

Users sign in with Entra ID and see answers whose content depends on their roles and groups. Every authorization decision (which documents can be retrieved, which tools can run, who may approve an action) must be made on the server from the user's token, never in the browser and never by the model.

A common shape is a single-page app in the browser calling a separate API. The browser then has to obtain, hold and refresh access tokens for that API, and the system has two deployables, two app registrations, cross-origin rules, and streaming of the agent's progress across a service boundary.

## Decision

**One deployable: a Blazor Web App with interactive server rendering**, which acts as its own backend for the browser.

- Tokens never reach the browser. The server holds the session as an encrypted cookie and keeps the signed-in user in a scoped `IUserContext`.
- The agent's progress streams over the connection Blazor already maintains.
- Pages and endpoints call application services through one contract, so the UI never reaches into the agent, tools or data directly. Splitting out an API later is a hosting change, not a rewrite.

**Sign-in uses an ID token only, so the app registration has no client secret or certificate.** The app reaches every Azure service with its managed identity and never calls an API on a user's behalf, so it needs proof of who the user is, not a token to act as them.

**Access is assignment-based.** The enterprise app requires a role assignment, so Entra refuses anyone who hasn't been assigned before they reach the app. Every page and endpoint then requires an app role unless it's explicitly public, as the health endpoints are.

## Consequences

- No credential to store, rotate or leak for sign-in, and no tokens in the browser.
- A Blazor Server circuit holds memory per connected user and needs session affinity, which suits an internal tool rather than high public traffic.
- If the app ever needs to call an API as the user, sign-in moves to the authorization code flow with the app's managed identity as a federated credential, which is still secret-free.
- The app registration, groups and users are tenant-level objects, so they're created by a person with directory rights (`scripts/identity.sh`), alongside the bootstrap.

## Alternatives considered

- **Single-page app with a separate API.** Puts tokens in the browser and doubles the moving parts, with no benefit for an internal tool of this size.
- **Blazor WebAssembly.** Also puts tokens in the browser.
- **App Service built-in authentication.** Simple, but the app needs the user's roles and groups in its own authorization model, and the built-in option keeps that outside the code.
- **Authorization code flow with a client secret.** A credential to manage for capabilities the app doesn't use.
