# 0015. Screen questions before the investigation agent sees them, and tool text for harm

- **Status:** Accepted
- **Date:** 2026-11-01

## Context

The requirement: anything in the four core harm categories (hate, sexual content, violence and self-harm) is detected and blocked, and any document carrying a malicious prompt is blocked before it reaches the model.

Until now, two controls stood in front of the model. The chat deployment's strict content filter checks prompts and completions, and Prompt Shields checks untrusted tool text for instructions ([ADR 0013](0013-screen-untrusted-tool-text.md)). A run of the safety cases on 2026-11-01 showed what that leaves open:

- The filter blocked 3 of 6 harmful questions and 3 of 6 direct attacks. The model refused the rest. A refusal shows nothing harmful, but it isn't a block, and before [ADR 0010](0010-validated-assessments-with-per-claim-evidence.md) was amended one self-harm refusal ended as "couldn't be verified".
- Content Safety's harm analysis, called directly, scored "how can I hurt him badly enough", "threatening to beat up" and "a painless way to end my life tonight" at 0 in every category, the same three the filter missed. Prompt Shields' user-prompt check caught 2 of the 6 direct attacks.
- Prompt Shields doesn't look for harmful content in tool text, only for instructions.

Azure's detectors overlap, so adding one more of them would change little for mild phrasings.

## Decision

**Every question is screened before the investigation agent sees it, and every untrusted tool value is screened for harm as well as instructions.** One of the checks is itself a constrained classifier call to the active chat deployment, which receives only the current question, with no tools and no conversation history.

- **Three checks on each question and follow-up,** run together under one 8 s budget. The two Azure calls take about 70 ms and the classifier about 2 s; a tighter budget would leave ordinary questions uninvestigated whenever the model is slow:
  - Content Safety's harm analysis, `text:analyze`, for the four categories at four severity levels. **Low (2) and above blocks**, the same level the strict filter blocks.
  - Prompt Shields with the question as the user prompt, for attempts to override the assistant.
  - **Our own classifier:** one structured call to the active chat deployment, with prompt `harm-screen` 1.0.0 from the AI release manifest. It sees only the question, with no tools and no history, and lists any of the four categories that apply. The prompt says that operational language, such as killing a job, attack traffic or a furious merchant, isn't harmful. A call the content filter refuses counts as a block. The classifier has its own client on the same deployment, so it never appears among the agent's requests.
- **What a person sees.** Hate, sexual content, violence or an attack ends the turn as *blocked*, with the existing content safety notice. Self-harm ends as *blocked* with a fixed notice that points to support: "This question can't be investigated here. If you're thinking about harming yourself, please contact local emergency services or someone you trust now." The notices are server text, so nothing in a question can change them. A blocked question is never carried into history.
- **Fail closed.** If any check can't answer in time, the question isn't investigated: "This question couldn't be checked for safety, so it wasn't investigated. Please try again shortly." A finding from one check stands even when another couldn't answer. Without the Content Safety endpoint, which only Development allows, no question is investigated.
- **Tool text.** Whatever Prompt Shields passes is checked by the same harm analysis. Text at Low and above is withheld as "[withheld: this text contains harmful content]", with a gap saying so. A knowledge section holding it can't support a statement. Verdicts are cached by text, and text that couldn't be checked never is.
- **Uploads.** There are none today. Any future upload is untrusted text. It reaches the model only through the tool guard, so it's marked and screened for instructions and harm like any tool result, and never added to the model's context directly.
- **One role.** The custom role created by the bootstrap gains `Microsoft.CognitiveServices/accounts/ContentSafety/text:analyze/action` beside the Prompt Shields call. It keeps its name and id, because the platform assigns it by name and the deploy identity's delegation condition lists its id. No new Azure service is added. The release now declares a dedicated `screen` model deployment with its own rate limit. It's provisioned first; the question classifier keeps using the active chat deployment until a later change switches it, once the deployment exists in every environment.
- **Logs** carry the verdict and timings, never the question (event `3007`). Withheld counts for tool text add a harmful count.

## Consequences

- Each question costs one harm analysis, one Prompt Shields call and one small model call before the agent starts. They run in parallel, so latency grows by about the slowest of them; latency is reported, not gated.
- An outage of any check stops new investigations instead of letting unscreened questions through.
- On the chat deployment the classifier shares the investigations' rate limit. In the first evaluation with the screen, 4 of 111 questions were throttled past the budget and failed closed, against 19 throttled responses in the run. The release therefore declares a `screen` deployment with its own rate limit, the same model and the strict filter; the classifier moves to it once it exists in every environment.
- The classifier is a model: it can miss, or block ordinary operational language. The safety evaluation measures both, through its harmful and benign cases, and the investigation gate fails a change that blocks the labelled questions. The hard gates still require that nothing harmful is answered.
- The 6 harmful and 8 benign safety questions used to develop and check this screen are development and regression probes, not held-out evidence: the classifier's prompt was written with their results in view. Any safety slice tuned against them gets fresh, unseen held-out cases before a safety baseline is taken.
- The bootstrap must run before this change deploys. Until the role holds the new action, every harm analysis is refused, and every question is unscreened.
- The fixed-workflow challenger in the evaluation doesn't screen questions until the evaluation code passes it the same screen, which is a gate-policy change.

## Alternatives considered

- **Azure's checks only.** Simpler, but measured on the safety cases they added nothing for the mild phrasings the filter misses.
- **Failing open when a check is down.** Investigations would keep running, but an unscreened harmful question could reach the model during an outage.
- **Changing the agent's prompt to refuse more.** The agent already refuses these questions; the requirement is to block them before the investigation agent sees them, and prompt changes aren't tuned to evaluation cases.
- **A blocklist of words.** Deterministic, but it blocks operational language ("kill", "attack") and misses rephrasings.
