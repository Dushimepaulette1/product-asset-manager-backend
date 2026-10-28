# Order cancellation: requirements review (Part 3)

Branch `claude-cancellation-order`, commits 6f74442..abbd1f7, reviewed as on disk on 2026-10-28.

- **Section 1** is the `requirements-reviewer` subagent's report, verbatim. It is a read-only agent with
  Read, Grep and Glob only, and it was run with the prompt in `~/.claude/plans/requirements-review-prompt.md`
  unchanged. The first run was stopped before it returned; this is the rerun.
- **Section 2** adds "My verdict": I re-read the cited code and tests for every row, and also checked
  git history, which the subagent could not read.

---

## 1. Subagent report (verbatim)

Summary: 24 met, 4 partly met (A3, B4, B5, C4), 1 unverifiable (B6); D10 recorded as met but only verifiable from file contents (git history not readable). No requirement is missing. The headline finding is B5: no test uses two DbContext instances on the same order with an asserted second-SaveChanges exception; every race test goes through the API/consumer with `BeforeSaveHook`, and the loser catches the exception internally.

| # | Requirement | Verdict | Evidence (file:line) | Notes |
|---|---|---|---|---|
| A1 | POST /api/orders/{id}/cancel, own Pending only; 200 with updated order, 404 missing/other user, 409 not Pending | met | src/Controllers/OrdersController.cs:62-85; src/Services/OrderService.cs:117-125,138; tests/ProductAssetManager.Api.Tests/ApiTests/Orders/CancelOrderTests.cs:26-43, 51-64, 82-90, 104-120, 123-133 | Approved deviation: 409 "kept changing" while still Pending (OrdersController.cs:79-81; CancelRaceTests.cs:99-128). |
| A2 | Cancelled final status; GET returns it | met | src/Models/OrderStatus.cs:8; src/Services/OrderService.cs:108; CancelOrderTests.cs:42-43 | "GET must return the cancelled status". |
| A3 | Consumer skips cancelled orders without changing stock, logs the skip, completes the message | partly met | src/Messaging/OrderConsumer.cs:143-147, 167-170, 240-243, 281-299; CancelledMessageProcessingTests.cs:35-39; CancelRaceTests.cs:149-154 | Skip, unchanged stock and log are asserted; completion is only inferred from a later same-SKU-session order being Confirmed, not asserted directly (no single-delivery or empty-queue check). |
| A4 | Order concurrency token; races handled; exactly one wins; never Cancelled with stock decremented | met | src/Models/Order.cs:27-28; src/Messaging/OrderConsumer.cs:186-211; src/Services/OrderService.cs:140-155; CancelRaceTests.cs:32-58, 131-167, 170-223 | Consumer writes status and stock in one SaveChanges; tests cover both directions plus a 20-order stress check. |
| A5 | orders.processed outcome=cancelled; cancelled excluded from Pending gauges; dashboard shows cancellations | met | src/Services/OrderService.cs:136; src/Observability/OrderMetrics.cs:54; src/Observability/PendingOrdersMetrics.cs:38,50; docker/grafana/dashboards/product-asset-manager.json:131-161, 344, 410-418; CancelOrderTests.cs:46; PendingOrdersMetricsTests.cs:16-31; DashboardTests.cs:30-37, 59-72 | Gauge test sets Cancelled directly in the DB (PendingOrdersMetricsTests.cs:24), not through the endpoint. |
| A6 | Tests for success, ownership, invalid states, cancelled-message processing, concurrency | met | CancelOrderTests.cs:26, 51, 104, 123; CancelledMessageProcessingTests.cs:22; CancelRaceTests.cs:32, 131 | All five areas covered. |
| B1 | WAF NUnit test: Pending, cancel returns 200, order Cancelled | met | CancelOrderTests.cs:12, 26-43; ApiTestBase.cs:29 | Via `ApiTestBase` / `CustomWebApplicationFactory`. |
| B2 | WAF NUnit test: Confirmed, cancel returns 409 | met | CancelOrderTests.cs:104-120 | `[TestCase(OrderStatus.Confirmed, 1)]`; status unchanged. |
| B3 | WAF NUnit test: other user's order returns 404 | met | CancelOrderTests.cs:51-64 | Also asserts the order stays Pending. |
| B4 | Test: consumer skips cancelled order, no stock decrement, completes message | partly met | CancelledMessageProcessingTests.cs:22-55 | Unchanged stock is asserted at :38; completion is inferred from the later order resolving (:35), with no direct check such as `ProcessSpansFor(...)` Count 1, as DeletedRowDuringProcessingTests.cs:47 does. |
| B5 | Two separate DbContext instances load the same order; one confirms, one cancels; second SaveChanges throws a concurrency exception; first outcome preserved | partly met | CancelRaceTests.cs:32-58, 131-167; tests/ProductAssetManager.Api.Tests/ApiTests/BeforeSaveHook.cs:25-40; src/Services/OrderService.cs:140; src/Messaging/OrderConsumer.cs:193 | No test does this literally. The only `ThrowsAsync` is a `DbUpdateException` for an FK (DeletedRowDuringProcessingTests.cs:63). Races go through the API/consumer with `BeforeSaveHook`; the loser catches `DbUpdateConcurrencyException` internally, so the exception is never asserted. The first outcome being preserved is asserted. |
| B6 | All prior tests still pass | unverifiable | n/a | Cannot run tests; no code evidence of a regression. |
| C1 | `OrderStatus.Cancelled` appended last; `Order.RowVersion` [Timestamp]; new migration adds only the column | met | src/Models/OrderStatus.cs:5-8; src/Models/Order.cs:27-28; src/Data/Migrations/20261027122304_AddOrderConcurrencyToken.cs:11-28; src/Data/Migrations/ApplicationDbContextModelSnapshot.cs:289 | Confirmed is still 0; Up only adds `RowVersion`, Down only drops it. |
| C2 | `OrderMapper` extracted, used by GET and cancel | met | src/Services/OrderMapper.cs:8-18; src/Services/OrderService.cs:108, 138; CancelOrderTests.cs:34-43 | |
| C3 | Consumer re-checks status before every attempt; reloads order and variant after a conflict | met | src/Messaging/OrderConsumer.cs:167-170, 203-204; CancelRaceTests.cs:150-158, 247-253 | Stock-conflict test shows no double decrement (stock 2, Rejected). |
| C4 | `OrderCancelled()`: counter only, no duration, pre-created zero series | partly met | src/Observability/OrderMetrics.cs:48, 54; OrderMetricsTests.cs:77-86 | Counter-only and no-duration are asserted; no test asserts the zero `cancelled` series at OrderMetrics.cs:48. |
| C5 | Test-only `BeforeSaveHook`; deterministic race tests in both directions | met | BeforeSaveHook.cs:8-41; CustomWebApplicationFactory.cs:18, 64; CancelRaceTests.cs:32-58 (consumer wins), 131-167 (cancel wins) | Cancel-wins asserts `save_attempts` = 1 on the stock-check span (:158), consistent with the approved "counts actual saves" deviation. |
| C6 | Dashboard changes plus `DashboardTests` | met | product-asset-manager.json:131-161 (id 14, orange), 39-41/89-91/139-141/189-191/239-241/305-307 (w 4, x 0 to 20), 344, 380-418; DashboardTests.cs:22-72 | Panel id 14 is the approved deviation. |
| C7 | README and CLAUDE.md document the feature | met | README.md:19, 58, 76-77, 87, 276, 323-325, 369-372, 588-594, 685-692; CLAUDE.md:73-76 | Docs only; no test applies. |
| D1 | Thin controllers | met | src/Controllers/OrdersController.cs:62-85 | One `CancelAsync` call, then status mapping. |
| D2 | DTOs at boundary via mapper | met | src/Services/OrderService.cs:138; src/Services/OrderMapper.cs:8; CancelOrderTests.cs:34 | Returns `OrderResponse`, never the entity. |
| D3 | Services use DbContext directly; typed result records | met | src/Services/OrderService.cs:15, 111-160; src/Services/Results/OrderResults.cs:10 | Concurrency exception caught inside the service; CancelRaceTests.cs:53, 125 assert no unhandled-exception log. |
| D4 | API tests use NUnit and WAF (`CustomWebApplicationFactory` / `ApiTestBase`) | met | CancelOrderTests.cs:12; CancelRaceTests.cs:18; CancelledMessageProcessingTests.cs:10; DeletedRowDuringProcessingTests.cs:14; PendingOrdersMetricsTests.cs:13 | `DashboardTests` is a plain unit test, consistent with the plan. |
| D5 | `ILogger` structured templates | met | src/Messaging/OrderConsumer.cs:195-199, 231-234, 287-289; CancelRaceTests.cs:283-287; ApiTestBase.cs:151-153 | Tests match on `log.Body == template`. |
| D6 | No personal data in logs or spans | met | src/Messaging/OrderConsumer.cs:195-199, 285-289; src/Services/OrderService.cs:111-160 | From code only, no test asserts the absence. New logs and tags carry only order id, attempt, entity type names and outcome. |
| D7 | Low-cardinality metric tags | met | src/Observability/OrderMetrics.cs:48, 54; OrderMetricsTests.cs:83-84 | The cancelled measurement has only an `outcome` tag. |
| D8 | No applied migration modified; new one created | met | src/Data/Migrations/20261027122304_AddOrderConcurrencyToken.cs:13-27; 20260914131615_AddOrderStatusAndVariantConcurrencyToken.Designer.cs:394 | From file contents only: the older designer shows only the Variant RowVersion, consistent with it being untouched. Git history not readable. |
| D9 | FKs Restrict except `ProductCollection` | met | src/Data/ApplicationDbContext.cs:74, 79, 88, 94; DeletedRowDuringProcessingTests.cs:54-66 | The feature adds no new FKs. |
| D10 | No Claude attribution in commits or PRs | met | n/a | Unverifiable for git history; no repo file contains attribution text, and the only match is the rule itself in CLAUDE.md. |

### Gaps
- **A3:** the test proves completion of the cancelled order's message only indirectly, by a later same-session order resolving; nothing asserts single delivery or an empty queue.
- **B4:** same reason as A3. A one-delivery check like DeletedRowDuringProcessingTests.cs:47 would close it.
- **B5:** no test loads the same order in two separate DbContext instances and asserts that the second SaveChanges throws `DbUpdateConcurrencyException`. The race tests use the API/consumer plus `BeforeSaveHook`, and the loser swallows the exception, so the exception itself is never asserted.
- **C4:** no test asserts the pre-created zero-valued `cancelled` series (OrderMetrics.cs:48).
- **B6:** cannot be verified without running the test suite.

### Sources
Given and found: Part 2 requirements A1–A6 and testing requirements B1–B6 (verbatim in the prompt); the approved plan `/home/paulette/.claude/plans/pasted-content-id-fc46-we-have-squishy-mist.md` plus the four approved deviations listed in the prompt; `/mnt/c/Users/Green Way/CSharp_Training/product-asset-manager-backend/CLAUDE.md`. Not available: a Part 2 assignment document (none in the repo, as stated); git history (affects D8 and D10).

---

## 2. Independent verification: My verdict

I agree with all 29 verdicts. I made no changes and did not run the test suite. I refine three of the
subagent's reasons (B5, D8, D10), and I note one place where the verdict depends on the rubric (A3).

| # | Reviewer | My verdict | Notes (reason given for every refinement or disagreement) |
|---|---|---|---|
| A1 | met | met | Confirmed: `[Authorize]` on the controller, owner check at OrderService.cs:117 (admins get 404 too, CancelOrderTests.cs:67-79), 409 at :122-125. |
| A2 | met | met | Confirmed. Cancelled is final: a second cancel returns 409 (CancelOrderTests.cs:122-135), and the consumer skips it. |
| A3 | partly met | partly met | Agree under the reviewer's rubric ("a test asserts it exactly"). The production code fully meets A3: `SkipNonPendingAsync` logs and completes at OrderConsumer.cs:283-299. The gap is in the test only. Inferring completion from the later order is weak, because a session processor still receives the next message while an unsettled one holds its lock. |
| A4 | met | met | Confirmed. Status and stock change in one SaveChanges, and the Order RowVersion conflict rolls back both, so a Cancelled order with stock decremented cannot exist. Both race directions are asserted. |
| A5 | met | met | Confirmed. Setting the status directly in the gauge test is fine: the gauges filter only on `Status == Pending` (PendingOrdersMetrics.cs:38, 50). |
| A6 | met | met | Confirmed. |
| B1 | met | met | Confirmed (CancelOrderTests.cs:25-48). |
| B2 | met | met | Confirmed (CancelOrderTests.cs:104-120, Confirmed case). |
| B3 | met | met | Confirmed (CancelOrderTests.cs:50-65). |
| B4 | partly met | partly met | Agree. Same reason as A3: the test has no `ProcessSpansFor(cancelledOrderId)` single-delivery assertion. |
| B5 | partly met | partly met | **Agree with the verdict; the stated reason needs correcting.** (1) There *are* two separate DbContext instances on the same order: the API request scope and the consumer's scope each load it, one confirms and one cancels. What is missing is that the *test* doesn't own them or call SaveChanges itself. (2) "The exception is never asserted" overstates it: the exception is proven indirectly. The cancel-wins test asserts the conflict log (CancelRaceTests.cs:153), which is written only inside `catch (DbUpdateConcurrencyException)` at OrderConsumer.cs:193-199. In the consumer-wins test, `HasFired` plus the "is Confirmed" 409 (:48-50) can only come from the post-catch path at OrderService.cs:152-154, because the pre-check saw Pending. (3) Against the literal wording, though, no test asserts `Throws<DbUpdateConcurrencyException>` on a second SaveChanges, so partly met stands. |
| B6 | unverifiable | unverifiable | Agree. I did not run the suite this session. The project memory records 86/86 passing when the feature was committed, but I have not re-verified that. |
| C1 | met | met | Confirmed: enum order Confirmed=0 … Cancelled=3; `[Timestamp]` at Order.cs:27; the migration Up/Down only adds and drops `RowVersion`. |
| C2 | met | met | Confirmed. |
| C3 | met | met | Confirmed: the status re-check at OrderConsumer.cs:167 runs before every attempt; both entities are reloaded at :203-204. |
| C4 | partly met | partly met | Agree: the zero series exists at OrderMetrics.cs:48, but no test asserts it. |
| C5 | met | met | Confirmed: the hook pauses inside `SavingChangesAsync` (BeforeSaveHook.cs:25-40), and both directions are covered. |
| C6 | met | met | Confirmed: panel id 14, the query shape matches Rejected, orange override, the top row fills 24 columns. |
| C7 | met | met | Confirmed: CLAUDE.md:73-76; README mentions cancellation in 23 places. |
| D1 | met | met | Confirmed. |
| D2 | met | met | Confirmed. |
| D3 | met | met | Confirmed: `CancelOrderResult` record, and the exception is handled inside the service. |
| D4 | met | met | Confirmed. |
| D5 | met | met | Confirmed. The only interpolated strings added are HTTP response messages in the controller, not log templates. This matches the existing GET endpoint. |
| D6 | met | met | Confirmed by scanning every added log and span call in the feature diff: no emails, names, user ids or tokens. |
| D7 | met | met | Confirmed. |
| D8 | met | met | **Agree, with stronger evidence.** `git diff --stat 6f74442^ HEAD -- src/Data/Migrations` touches only the new `AddOrderConcurrencyToken` migration, its designer, and the snapshot (+6 lines). No existing migration changed. |
| D10 | met | met (commits) | **Agree, with stronger evidence, but the subagent broke its own instructions.** It was told to mark D10 unverifiable, yet marked it met. I checked git: none of the 7 feature commits contains Co-Authored-By, Claude or "Generated with". No PR exists yet, so the PR half is still open. |

### Gaps that remain after verification

All of these are test gaps. The production code meets every A requirement.

1. **B5 (main gap):** no test-owned two-DbContext test with an explicit `DbUpdateConcurrencyException`
   assertion on the second SaveChanges.
2. **A3 / B4:** no direct single-delivery assertion for the cancelled order's message
   (for example, `ProcessSpansFor(cancelledOrderId)` should have Count 1).
3. **C4:** no assertion for the pre-created zero `cancelled` series.
4. **B6:** the full suite has not been re-run in this session.

Nothing has been fixed or committed.

---

## 3. Resolution

Sections 1 and 2 above are the review as it was recorded and have not been changed. This section adds
how each gap was closed. All test changes are in one commit, separate from this document and the agent
definition: **`6c6ebeb`** (`6c6ebeb5be04d680fefb5499668beae5e20dd191`), "test: close cancellation review gaps".
Production code was not changed.

| # | Reviewer | My verdict | After fix | Fixing commit | Evidence (file:line) |
|---|---|---|---|---|---|
| B5 | partly met | partly met | met | `6c6ebeb` | `CancelRaceTests.cs:170-195`: two DI scopes give two `ApplicationDbContext` instances (asserted not the same); both load the same Pending order; one saves Confirmed; the other's save of Cancelled is asserted to throw `DbUpdateConcurrencyException` (:190); the database still holds Confirmed (:194). |
| A3 | partly met | partly met | met | `6c6ebeb` | `CancelledMessageProcessingTests.cs:40`: `ProcessSpansFor(cancelledOrderId)` has Count 1, so the message was completed on its first delivery. This follows the pattern in `DeletedRowDuringProcessingTests.cs:47`. |
| B4 | partly met | partly met | met | `6c6ebeb` | Same assertion as A3 (`CancelledMessageProcessingTests.cs:40`). |
| C4 | partly met | partly met | met | `6c6ebeb` | `OrderMetricsTests.cs:89-117`: building `OrderMetrics` records exactly one `orders.processed` measurement tagged `outcome=cancelled`, with value 0. The test uses its own listener because the fixture's listener enqueues one entry per counted order and so drops zeros. |
| B6 | unverifiable | unverifiable | met | n/a (verified at `6c6ebeb`) | Full suite run on 2026-10-28 with `dotnet.exe test ProductAssetManager.slnx` against LocalDB and the Service Bus emulator, with the Compose `api`/`consumer` containers not running. Result: **Passed 88, Failed 0, Skipped 0** (86 pre-existing + 2 new). |

Checks run before committing `6c6ebeb`: `dotnet.exe build` reported 0 errors and no compiler warnings (only the
existing NU1902/NU1903 advisories for `System.Security.Cryptography.Pkcs`), the full test suite passed
as above, and `git diff --check` was clean.

D10 at `6c6ebeb`: that commit carries no Claude attribution either. The PR has not been created yet.
