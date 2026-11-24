# 0022. An enterprise UI on MudBlazor, with a role-layered home and an Admin pause

- **Status:** Accepted
- **Date:** 2026-11-16

## Context

The investigation flow worked, but the UI was unstyled template HTML on one page, so the system read as a prototype even where it wasn't. It's an internal tool for a payments operations team: people arrive through company sign-in and already know what it does, so it needs to look deliberate and stay out of the way rather than explain itself. It also needs to show what each role can do. Higher roles hold more rights than the ones below them, but only an approval control showed that, and the only pause was an app setting that needs portal access.

The UI must keep what the server already guarantees: model text is bound as text, every decision is re-checked on the server, and nothing moves to the browser.

## Decision

**MudBlazor with a custom theme, every page interactive except the few that must stay static, a home page built per role on the server, and a pause switch for Admins that the server checks on every change.**

- **MudBlazor, themed as Calderbrook Payments.** It has the components the UI needs (layout, dialogs, buttons, progress) and runs under Interactive Server rendering. The theme sets one palette per mode, one type scale (Inter, served by the app) and fixed radii, so pages can't drift. A unit test computes WCAG contrast for every text colour on every background it's used on, in both modes, and a test fails if the stylesheet uses a colour the theme doesn't define. Fluent UI Blazor was the alternative; MudBlazor was preferred because its components cover the UI and a custom theme reads as this product rather than as Office. (Amended 2026-11-24: the fictional company was Contoso Payments, a name Microsoft's own samples use, so the product read as one of them. Only the name changed; the theme, its colours and its tests are the same. One policy in the evaluated knowledge still says Contoso, since changing it would need a paid evaluation.)
- **Global Interactive Server routing,** so moving between Home and Investigate keeps one circuit. The pages that must set a status code or post a plain form stay static: Source, Signed out, No access, Not found and Error.
- **A circuit-scoped investigation session.** It holds what the Investigate page shows and the running investigation, so leaving the page doesn't cancel the run or lose the result or a pending ticket card. It decides nothing the server owns: the conversation stays in the investigation service, proposals in the approval service, and a card is actionable only while the stored proposal is pending and unexpired. A refresh, sign-out or the circuit ending still clears it; nothing is persisted.
- **Sources open in a new tab,** so following a citation never disturbs the investigation.
- **Theme without a flash.** The person's choice is a cookie the server reads when it renders. Without one, a small script in the page head records the OS preference for the next request and marks the page, which the stylesheet uses to paint the right palette before the app starts. Static pages, which never start the app, get their dark palette from the same mark.
- **A home page layered by role.** Everyone sees processor incidents, settlements needing attention and their own usage. Engineers add tickets waiting for their approval and recent tickets; Admins add an operations section. The overview is built on the server, and a section a role doesn't get is never fetched. Each added section names the role that adds it. Demo data is read as of the demo clock, so the page agrees with what an investigation finds. A row's Investigate fills a fixed question built from the record; nothing runs until the person asks.
- **Admins pause and resume investigations from the home page.** The switch checks the Admin role on every change, so hiding the button isn't the control. It extends [ADR 0021](0021-bounded-usage-and-observable-cost.md)'s pause: the state is one row in the usage table, cached for 10 seconds on each instance and replaced at once on the instance that changed it, so new investigations stop within 10 seconds everywhere. A change is decided from a fresh read and written only if the row hasn't changed since, so two Admins can't overwrite each other unseen. A row that can't be read refuses investigations, as the counter does. The app setting still pauses whatever the switch says, and only a deployment clears it. Each change is logged with the pseudonymous id the usage rows use and the reason's length; the reason itself is kept only in the row.
- **No page before sign-in.** Unauthenticated requests go straight to Entra, as before. The app's own pages outside the signed-in experience are Signed out, No access, Not found and Error, each a plain card.

## Consequences

- One circuit holds more state per person: the session, its run and its results. It ends with the circuit, and disposing it cancels any run still going.
- The palette is defined in the theme and, for the dark mode of static pages and the first paint, repeated in the stylesheet. The stylesheet test bounds that repetition to the theme's own colours.
- MudBlazor writes inline styles. Today's Content Security Policy sets only `frame-ancestors 'none'`, so nothing breaks; a stricter policy would need `style-src 'unsafe-inline'` and MudBlazor's own script and font paths.
- A pause takes up to 10 seconds to reach other instances, and the panel says so. An unreadable pause row stops investigations until it can be read.
- The app bar isn't fixed, so on a phone it can wrap to keep Sign out reachable; it scrolls away on long pages.

## Alternatives considered

- **Fluent UI Blazor.** Comparable components; its default look is close to Microsoft's own products, which isn't what this product should look like.
- **Per-page interactivity, as before.** Leaving Investigate would end the circuit and lose a running investigation and a pending ticket.
- **Persisting the investigation's state.** Conversations, proposals and tickets are deliberately in memory for the demo; persisting only the page's copy would create a second, divergent record.
- **Pausing by changing the app setting from the UI.** It needs a management-plane role for the app's identity and restarts the app.
