# PinkieSysMon — AI Engineering Rules

This file governs AI-assisted work in the [PinkieSysMon repository](https://github.com/Jim1537/PinkieSysMon). It is a **workflow and evidence policy**, not an architectural specification, user manual, provider reference, widget dictionary, or issue tracker. Read the relevant [Application Architecture](docs/development/architecture.md) and subsystem documentation instead of restating them here.

## Establish current authority before acting

1. Identify the **actual target repository, branch or PR, and commit SHA**. Read the current instructions and relevant files at that revision. Do not substitute recalled content, previous chat results, or a filename for a fresh source read.
2. Inspect the precise source, tests, documentation, Issues, PR history, and acceptance evidence relevant to the requested change. Distinguish merged `main`, an unmerged branch, an older accepted baseline, and the user's private local installation.
3. Treat implementation source and executable tests as authorities on implemented contracts; actual observed Windows/build, Runtime, Editor, and hardware results are authorities only for the conditions tested. Official manufacturer documentation may establish published specifications, but cannot establish installed hardware state, actual device/driver behavior, or application acceptance. Use [GitHub Issues](https://github.com/Jim1537/PinkieSysMon/issues) for current unresolved work and [Development Roadmap](docs/development/roadmap.md) for historical milestones.
4. When source, docs, tests, and reported observations disagree, explain the specific conflict. Do not silently choose a convenient version or modify working source solely to fit older prose.
5. If current GitHub state cannot be retrieved, explicitly label any offline snapshot as such. Do not invent live status or perform irreversible updates based only on memory.
6. User authorization is required for repository mutations. If it is granted for an ongoing task, keep changes within that delegated scope and use reviewable commits/PRs.
7. Do not import implementation facts, hardware state, measurements, assumptions, or decisions from another user project without explicit permission. Limit any authorized import to its stated purpose and identify it as external context.

Existing higher-priority ChatGPT Project or platform bootstrap instructions remain binding until the user updates them; this repository file does not silently replace those instructions.

## Explicit permission to say "I don't know" / "не знаю"

**"I don't know" ("не знаю") is an explicitly acceptable, correct, and sometimes necessary answer.** Insufficient evidence is not a reason to manufacture an answer, select an unverified option, or sound more certain than the sources justify. Accuracy takes priority over apparent completeness.

- If relevant facts are unavailable, sources conflict without resolution, or necessary observations/tests have not been performed, **say what is not known**. Never present recollection, plausible inference, intended behavior, or a hypothesis as a verified fact.
- Distinguish **known from inspected evidence**, **inferred but unconfirmed**, **not verified**, **not tested**, and **unknown**. A code path can show what is implemented without proving its real-world behavior. Do not disguise missing evidence with arbitrary confidence percentages.
- Provide the supported part of an answer when possible, clearly separating it from the unknown part. Identify the minimum evidence or test that would resolve a material uncertainty.
- If an unknown is critical to a safe or correct implementation, diagnosis, merge, compatibility claim, or acceptance decision, do not fill it with a guess; stop that dependent step and explain the blocker.
- This permission is **not** an excuse to avoid research or inspection when authoritative sources and tools are available. Check what can reasonably be checked first, then say "I don't know" when the remaining gap is real.

## Contracts and changes

- Establish the existing behavior and acceptance boundary before making a fix. Prefer the smallest coherent **systemic** correction over a collection of compensating patches.
- Do not alter unrelated architecture as part of a local fix without a specific need. Preserve currently accepted Runtime/Editor boundaries and canonical dashboard semantics; read [Application Architecture](docs/development/architecture.md) for current system structure.
- Do not replace the production rendering path with WebView2/Chromium, use private WinForms `PropertyGrid`/`GridItem` reflection to style internals, rely on `BeginInvoke`/timing tricks as UI styling architecture, handcraft non-client chrome when native mechanisms suffice, or substitute arbitrary filename-character filters for Windows path-component validation.
- Provider-specific behavior is owned by [provider documentation](docs/telemetry/system.md); do not silently invent hardware/sensor identities, provider ownership, metric aliases, unit conversions, or cached valid readings after loss of availability. Preserve isolation and explicit unavailable values.
- Device selection and transport behavior are owned by [device integration documentation](docs/development/devices/trofeo-vision-9.16.md). Do not choose the first compatible device when identity is missing/ambiguous, or change protocol-sensitive I/O based on speculative performance theories. Require actual physical-device evidence for protocol/lifecycle changes.
- Handle errors, recovery, lifetime, ownership, and long-running overhead explicitly. Prefer supported Windows/.NET primitives over private hacks. Treat unknown host/driver/device root causes as **unknown**, not as proven application defects.
- During interactive live diagnostics, establish the observed state and propose one minimal evidence-producing step at a time. Verify the user's result before taking a dependent step; record only durable findings. Avoid long speculative command sequences while the user is operating the system.
- Never rewrite an established regression solely to make a failing implementation appear correct. When a reproducible behavior changes intentionally, update the tested contract with a documented reason.

## Schema and private production dashboard

- Treat a persisted dashboard schema change as a deliberate compatibility event: version it, implement deterministic migration, and add regression checks for semantic preservation and repeat parse/migrate/save idempotence.
- When validation needs the author's real `PinkiePie_Vertical` dashboard, use the **actual latest private user-supplied artifact**, not the public `Default` dashboard or a model reconstruction. Do not create a new production-dashboard archive when neither its schema nor its content changed.
- Preserve widget identity, geometry, layer/group interpretation, media/source contracts, and rendered meaning unless the request deliberately changes them.
- Do not place private production dashboards, local machine settings, logs, restricted icon/font packs, software credentials, signing secrets, or unnecessary device identifiers in the public repository. Inspect staged diffs and honor [Public Repository Boundary](docs/PUBLIC-REPOSITORY.md).

## GitHub change procedure

1. Confirm the exact branch/commit and existing Issue or user-requested scope.
2. Inspect the touched implementation and relevant regression contracts first.
3. Work on an appropriately scoped branch with a descriptive commit and PR when practical; do not default to passing around replacement source ZIPs. Use an archive only when explicitly requested or when an agreed offline transfer is necessary.
4. **Before** creating or modifying an artifact, audit source support, intended scope, semantic overlap, and the lasting value of its contents. **After** the operation, fetch and inspect the **actual resulting artifact** and complete diff: recheck structure, references, unintended source/schema changes, secrets, generated output, private assets, unrelated edits, and applicable verification gates. Intended output alone is not evidence of delivery.
5. Run the checks available for the change and report **observed** results. The source-of-truth Windows build/regression procedure is [`build.ps1`](build.ps1); a green documentation site job does not verify the app.
6. Merge only after the relevant acceptance conditions have actually been met. Do not close an Issue because a patch exists; record which outstanding gates remain.
7. Where a change affects an established documented contract, update its **single canonical document** in the same PR; do not create parallel copies.

## Validation vocabulary

Report each applicable dimension separately, attached to the exact commit/revision tested:

| Gate | What can establish it |
| --- | --- |
| **Static inspection** | Examined repository source and diff, no execution claim |
| **Package integrity** | Actual archive contents, forbidden/generated files, readability and SHA-256 inspected |
| **Compile** | Actual successful Windows project build; otherwise **NOT RUN** |
| **Regression** | Actual deterministic regression execution for this revision; otherwise **NOT RUN** |
| **Runtime** | Application behavior exercised; otherwise **NOT TESTED** |
| **Visual** | Editor/dashboard behavior observed; otherwise **NOT TESTED** |
| **Device** | Real-device behavior observed for the specified scenario; otherwise **NOT TESTED** |
| **Docs** | MkDocs build and, separately, published-site deployment actually observed |

Do not infer one gate from another. Historical passing test counts do not establish counts or results for a new commit. `PASS WITH WARNINGS` must not disguise an unresolved critical contract failure.

## Documentation ownership and maintenance

The user may provide exhaustive context; the assistant owns editorial selection. Retain details only when they materially support operation, correctness, compatibility, fault isolation, recovery, maintenance, auditability, or a real future engineering decision. Omit incidental history, personal provenance, and details easier to recheck than to maintain, unless technically consequential.

Use the single canonical owner for each type of information:

- **Cross-component topology and state ownership:** [Application Architecture](docs/development/architecture.md).
- **Individual telemetry providers:** [Telemetry documentation](docs/telemetry/system.md) and its sibling pages.
- **Widgets and properties:** [Widget documentation](docs/widgets/text-value.md) and [Property Dictionary](docs/widgets/properties.md).
- **Supported hardware and transport-specific contracts:** [Supported Devices](docs/supported-devices.md) and [Device Integration](docs/development/devices/trofeo-vision-9.16.md).
- **Build, packaging and installation:** [Building and Deployment](docs/development/building.md), with implementation in `build.ps1`.
- **Live work and acceptance:** GitHub Issues; **past milestones:** [Development Roadmap](docs/development/roadmap.md).
- **AI behavior/evidence and change workflow:** this file.

Duplicate content includes paraphrases with the same practical meaning. When a fact belongs to another document, **link instead of retelling it**. If ownership must change, move the fact and remove its old copy in the same change. Never restore revisioned `about`, buglist, provider, or widget Project attachments as competing current authorities.

## Delivery report

For each meaningful change, identify the target SHA, files changed, preserved/modified contracts, schema and real-private-dashboard impact, test gates actually run, outstanding acceptance or uncertainties, and PR/Issue links. Never claim a verification step was performed when it was only reasoned about.
