# 0011. The model proposes; the server executes after the owner approves

- **Status:** Accepted
- **Date:** 2026-10-22

## Context

When a settlement batch didn't close, the runbooks say to raise an incident with the processor, with an Operations Engineer's approval. Creating that ticket is the only action the system takes that changes anything outside itself. The agent can see what to raise, but it reads untrusted text from processors and from the corpus, and a model can be talked into a confident, well-formed request. Whoever approves must see exactly what will be created, and must still be allowed to create it at the moment they click.

## Decision

**The proposal is part of the assessment, and creating the ticket is a server action the model can't reach.**

- `submit_assessment` carries an optional `proposed_ticket`: one processor, the affected merchants, the settlement dates, a title and summary, and the evidence and knowledge behind it. It's validated with the rest of the assessment: every merchant and the processor must be records this investigation's tools returned, the ticket needs both data evidence and a runbook citation, dates are checked against the investigation's own today, missing files from more than one processor are a platform incident with no ticket, and the text must pass a content policy that reports rule ids, never matches.
- The processor's own incident reference comes from typed incident data registered by the tool, never from model text.
- For an Operations Engineer, the validated proposal is stored as an immutable server record with an owner, an expiry on real time and a state. A Reader's proposal stays a draft on their page: nobody could approve it.
- `ApproveAsync(proposalId)` looks up the stored record and checks, at that moment, that the caller is signed in, is an Engineer, owns it, and that it's still pending and unexpired. It then moves it to executing atomically and creates the ticket with the proposal id as the idempotency key. Supersession, rejection and expiry are states, so an old page can't approve anything.
- There's no ticket tool, no decision field the model can set, and no HTTP route for approvals: decisions go through the signed-in circuit only.

## Consequences

- No ticket exists without an owner's click on content the server stored, and a repeated or concurrent click makes one ticket.
- A human can still approve a well-formed proposal that untrusted text steered. Warnings for that come with the injection defences; the controls here keep it grounded and visible.
- Proposals and tickets are held in one process's memory, so they're lost on restart and the app runs as one instance ([ProductionGapAnalysis](../ProductionGapAnalysis.md)).
- A Reader's draft can't be handed to an Engineer; the Engineer runs the investigation themselves.

## Alternatives considered

- **Agent Framework's approval-required function.** It pauses the loop on a tool call and resumes with the decision. The decision then travels through the model's conversation, the ticket's content is whatever the model put in the call, and a paused run has to be kept alive across a person's think time.
- **A separate `propose_ticket` tool in the loop.** Gives the model a second exit and a way to propose before it has submitted what the proposal rests on. Keeping it inside the one submission validates both together.
- **Binding approval to a hash of the content.** Useful when content can change between display and decision, as with an HTTP form. Here the record is immutable and looked up by id, so a hash protects nothing; it's revisited if an approval route outside the circuit is ever added.
