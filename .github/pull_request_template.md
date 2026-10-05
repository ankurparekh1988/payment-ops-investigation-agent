## Summary

<!-- What capability does this PR add, and what proves it works? -->

## Change type

- [ ] Feature / capability
- [ ] Infrastructure (Terraform)
- [ ] CI/CD
- [ ] AI behaviour (manifest, prompts, tool descriptions, guardrails, thresholds)
- [ ] Documentation only
- [ ] Fix / refactor

## Definition of done

- [ ] Tests added or updated for this capability
- [ ] Terraform and configuration included where the capability needs them
- [ ] Affected documentation updated in this PR (Architecture, Security, Evaluation, Deployment, Runbook, README)
- [ ] `main` stays buildable and deployable

## AI change checklist

*Complete if any AI behaviour box above is ticked; otherwise delete this section.*

- [ ] **Manifest:** `ai/manifest.yaml` version bumped; the change is recorded
- [ ] **Prompts:** version bumped with a changelog entry; prompt lint and drift check pass
- [ ] **Tools:** contract, validation, authorization and `tool-descriptions.yaml` entry updated together; security review requested
- [ ] **Guardrails:** content filter or Prompt Shields change reviewed for safety impact
- [ ] **Thresholds:** evaluation threshold changes justified (thresholds change only via ADR)
- [ ] **Evaluation delta** attached (metrics before vs. after)
- [ ] **Cost delta** estimated (tokens per turn, cost per resolved investigation)

### Model change pre-screen

*Complete only when adding or changing a model deployment.*

- [ ] Available in the target region and deployment type
- [ ] Data-handling terms reviewed
- [ ] Retirement date at least 6 months away
- [ ] Supports tool calling and structured outputs
- [ ] Compatible with the content filter policy
- [ ] Price recorded in `ai/pricing.json`

## Security considerations

<!-- New identities, role assignments, untrusted inputs, data exposure. Write "None" if not applicable. -->
