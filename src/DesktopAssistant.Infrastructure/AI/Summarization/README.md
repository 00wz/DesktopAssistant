# ChatHistoryStructuredReducer

An implementation of `IChatHistoryReducer` (Semantic Kernel) that compacts conversation history by
asking an LLM to call a single structured tool — `submit_history` — with the reduced message list.

Unlike the built-in `ChatHistorySummarizationReducer`, which replaces a range of messages with a
single free-text summary, this reducer produces a proper list of `ChatMessageContent` objects,
preserving roles, function calls, and function results. That makes it compatible with agents that
use `FunctionChoiceBehavior.Required`, where function call / result pairs must always appear
together in the history.

---

## How it works

**Step 1 — Locate the compaction boundary**

`LocateSafeReductionIndex` finds the index of the rightmost `assistant` message that contains no
`FunctionCallContent`, searching backward from `chatHistory.Count - retainedMessageCount - 1`.
This is the last message included in the compaction range. Returns `-1` (no compaction) when the
history is too short or no such message exists.

Ending on an assistant message (rather than, say, a tool message) serves two purposes:
- Keeps every function_call / function_result pair on one side of the boundary.
- Avoids a `tool → user` sequence at the start of the retained tail, which strict providers
  (Gemini, Mistral) reject.

**Step 2 — Call the LLM with `submit_history`**

A temporary chat history is built for the LLM:
- System message: `ReduceSystemPrompt` — hard constraints only (what must never be violated)
- The messages in the compaction range (index `firstNonSystemIndex` through `truncationIndex`, inclusive)
- User message: `ReduceUserMessage` — the compaction task description appended after the history

The LLM is called with `FunctionChoiceBehavior.Required([submit_history], autoInvoke: false)`, so
it must respond by calling `submit_history` exactly once. The function is never auto-invoked;
its arguments are read directly from the response.

**Step 3 — Parse, validate, assemble**

The `messages` argument is deserialized to `List<HistoryMessageDto>` and validated:
- Every `function_call` id must have exactly one matching `function_result` call_id, and vice-versa.
- Each assistant message containing `function_call` items must be immediately followed by the
  corresponding `tool` messages in the same order (strict adjacency).

The validated DTOs are mapped back to `ChatMessageContent` via `HistoryMessageDtoMapper`.
The final history is assembled as:
```
[original system message] + [compacted messages] + [retained tail]
```

---

## Constructor

```csharp
new ChatHistoryStructuredReducer(
    IChatCompletionService service,
    int retainedMessageCount = 0,
    int? thresholdCount = null)
```

| Parameter | Description |
|---|---|
| `service` | Chat completion service used for the compaction call. |
| `retainedMessageCount` | Number of messages at the tail to keep verbatim. Everything to the left of this boundary is sent for compaction. `0` (default) = compact everything (subject to pair-safety). Must be non-negative. |
| `thresholdCount` | Best-effort hysteresis: reduction is skipped unless `chatHistory.Count - retainedMessageCount >= thresholdCount`. Note that because the LLM is not guaranteed to reduce message count, this guard may not prevent repeated calls. |

---

## Properties

| Property | Default | Description |
|---|---|---|
| `ReduceSystemPrompt` | Built-in prompt | System message establishing hard constraints (what must never be violated). Intentionally brief — task instructions belong in `ReduceUserMessage`. |
| `ReduceUserMessage` | Built-in prompt | User message appended after the history that describes the compaction task and available strategies. Keeping it separate from the system prompt improves instruction-following on most providers. |
| `FailOnError` | `true` | When `true`, exceptions during reduction propagate to the caller. When `false`, `null` is returned on failure (SK interprets `null` as "no reduction performed"). |

---

## DTO layer

Messages are exchanged with the LLM as a flat JSON array described by a JSON Schema embedded in
the `submit_history` function definition. The schema is intentionally `$ref`-free and `oneOf`-free
for compatibility with both OpenAI strict mode and Anthropic.

| DTO | Purpose |
|---|---|
| `HistoryMessageDto` | One message: `role` + `items` |
| `HistoryContentItemDto` | One content block: `type` discriminator (`text`, `function_call`, `function_result`) + type-specific fields |

`HistoryMessageDtoMapper` handles both directions:
- `ToDto` — converts `ChatMessageContent` → `HistoryMessageDto` (unknown `KernelContent` types are skipped)
- `FromDto` — reconstructs `ChatMessageContent` from `HistoryMessageDto` (unknown item types are skipped)

---

## Usage example

```csharp
var reducer = new ChatHistoryStructuredReducer(
    chatCompletionService,
    retainedMessageCount: 20,
    thresholdCount: 10)
{
    FailOnError = false
};

var reduced = await reducer.ReduceAsync(chatHistory, cancellationToken);
// reduced is null  → history was not long enough, use chatHistory as-is
// reduced is not null → use reduced instead of chatHistory
```

---

## Files

| File | Contents |
|---|---|
| `ChatHistoryStructuredReducer.cs` | Main reducer class |
| `HistoryMessageDto.cs` | `HistoryMessageDto` and `HistoryContentItemDto` records |
| `HistoryMessageDtoMapper.cs` | Bidirectional mapper between SK types and DTOs |
