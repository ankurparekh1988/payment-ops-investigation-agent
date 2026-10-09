# 0003. Server-rendered UI as its own backend, with credential-free sign-in

- **Status:** Accepted
- **Date:** 2026-10-09

## Context

Users sign in with Entra ID and see answers whose content depends on their roles and groups. Every authorization decision (which documents can be retrieved, which tools can run, who may approve an action) must be made on the server from the user's token, never in the browser and never by the model.

A common shape is a single-page app in the browser calling a separate API. The browser then has to obtain, hold and refresh access tokens for that API, and the system has two deployables, two app registrations, cross-origin rules, and streaming of the agent's progress across a service boundary.

## Decision

**One deployable: a Blazor Web App with interactive server rendering**, which acts as its own backend for the browser.

- Tokens stay on the server. The browser carries only a one-time authorization code; the server redeems it, holds the session as an encrypted cookie and keeps the signed-in user in a scoped `IUserContext`.
- The agent's progress streams over the connection Blazor already maintains.
- Pages and endpoints call application services through one contract, so the UI never reaches into the agent, tools or data directly. Splitting out an API later is a hosting change, not a rewrite.

**Sign-in uses the authorization code flow with PKCE, and the deployed app has no stored credential.** It redeems codes as its user-assigned managed identity, which the app registration trusts through a federated identity credential (`SignedAssertionFromManagedIdentity` in Microsoft.Identity.Web). The implicit flow is not enabled. A laptop has no managed identity, so local development uses a separate, short-lived client secret kept in .NET user-secrets.

**Access is assignment-based.** The enterprise app requires a role assignment, so Entra refuses anyone who hasn't been assigned before they reach the app. Every page and endpoint then requires an app role unless it's explicitly public, as the health endpoints are.

## Consequences

- The deployed app has no sign-in credential to store, rotate or leak, and no tokens reach the browser.
- The local-development secret is the one credential on the app registration. It exists only while local addresses are configured and expires after 30 days by default.
- A Blazor Server circuit holds memory per connected user and needs session affinity, which suits an internal tool rather than high public traffic.
- If the app needs to call an API as the user, the same sign-in already provides the tokens.
- The app registration, groups and users are tenant-level objects, so they're created by a person with directory rights (`scripts/identity.sh`), alongside the bootstrap.

## Alternatives considered

- **Single-page app with a separate API.** Puts tokens in the browser and doubles the moving parts, with no benefit for an internal tool of this size.
- **Blazor WebAssembly.** Also puts tokens in the browser.
- **App Service built-in authentication.** Simple, but the app needs the user's roles and groups in its own authorization model, and the built-in option keeps that outside the code.
- **ID token only (implicit flow).** Needs no credential at all, but Microsoft advises against the implicit grant for new apps, and the token passes through the browser.
- **Authorization code flow with a client secret in Azure.** A long-lived credential to store and rotate, which the managed identity makes unnecessary.
