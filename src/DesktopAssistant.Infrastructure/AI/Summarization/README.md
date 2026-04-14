# Chat History LLM Reducers

Two implementations of `IChatHistoryReducer` (Semantic Kernel) that compact conversation history
by asking an LLM to call a single structured tool — `submit_history` — with the reduced message list.

Unlike the built-in `ChatHistorySummarizationReducer`, which replaces a range of messages with a
single free-text summary, these reducers produce a proper list of `ChatMessageContent` objects,
preserving roles, function calls, and function results. That makes them compatible with agents that
use `FunctionChoiceBehavior.Required`, where function call / result pairs must always appear
together in the history.

Both reducers share a common base class (`ChatHistoryLlmReducerBase`) that handles all orchestration
(boundary detection, LLM invocation, JSON parsing, result assembly) and differ only in schema,
prompts, validation, and mapping.

---

## Two strategies

### 1. ChatHistoryStructuredReducer (tool_interaction)

Uses a simplified schema where a function call and its result are represented as a **single
`tool_interaction` item** inside an assistant message.

**Pros:**
- The LLM cannot forget to emit a matching result — both halves live in the same item
- No id/call_id correlation needed
- Simpler schema → fewer LLM errors
- No normalization of quirky model output required

**Cons:**
- The schema diverges from the native SK/OpenAI message format
- Less familiar to models trained heavily on OpenAI function calling format

**Item types:** `text`, `tool_interaction`
**Roles:** `user`, `assistant`

### 2. ChatHistoryPairedCallReducer (function_call / function_result)

Uses the traditional paired schema where `function_call` and `function_result` are separate items
correlated by `id` / `call_id`, closer to the native OpenAI / Semantic Kernel format.

**Pros:**
- Closer to the format models see during training
- Roles match the actual SK representation (`user`, `assistant`, `tool`, `system`)

**Cons:**
- The LLM must maintain id/call_id pairing correctly
- Requires strict adjacency validation (assistant → tool messages in order)
- Needs normalization for common model quirks (multi-result tool messages, id/call_id confusion)

**Item types:** `text`, `function_call`, `function_result`
**Roles:** `user`, `assistant`, `tool`, `system`

---

## How it works (shared logic in ChatHistoryLlmReducerBase)

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
- System message: `ReduceSystemPrompt` — hard constraints specific to the chosen strategy
- The messages in the compaction range (index `firstNonSystemIndex` through `truncationIndex`, inclusive)
- User message: `ReduceUserMessage` — the compaction task description (shared between strategies)

The LLM is called with `FunctionChoiceBehavior.Required([submit_history], autoInvoke: false)`, so
it must respond by calling `submit_history` exactly once. The function is never auto-invoked;
its arguments are read directly from the response.

**Step 3 — Parse, validate, assemble**

The `messages` argument is deserialized to `List<HistoryMessageDto>` and passed to the
strategy-specific `ValidateDtos` and `MapToMessages` methods. The final history is assembled as:
```
[original system message] + [compacted messages] + [retained tail]
```

---

## Constructor (same for both reducers)

```csharp
new ChatHistoryStructuredReducer(
    IChatCompletionService service,
    int retainedMessageCount = 0,
    int? thresholdCount = null)

new ChatHistoryPairedCallReducer(
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

## Properties (inherited from ChatHistoryLlmReducerBase)

| Property | Default | Description |
|---|---|---|
| `ReduceSystemPrompt` | Strategy-specific | System message establishing hard constraints. Each strategy defines its own default. |
| `ReduceUserMessage` | Built-in (shared) | User message appended after the history that describes the compaction task. Shared between strategies because it does not reference a specific schema. |
| `FailOnError` | `true` | When `true`, exceptions during reduction propagate to the caller. When `false`, `null` is returned on failure (SK interprets `null` as "no reduction performed"). |

---

## DTO layer

Messages are exchanged with the LLM as a flat JSON array described by a JSON Schema embedded in
the `submit_history` function definition. The schema is intentionally `$ref`-free and `oneOf`-free
for compatibility with both OpenAI strict mode and Anthropic.

| DTO | Purpose |
|---|---|
| `HistoryMessageDto` | One message: `role` + `items` |
| `HistoryContentItemDto` | One content block: `type` discriminator + type-specific fields. Contains the union of all fields used by both strategies (`Id`, `CallId` for paired-call; all fields for tool_interaction). Unused fields are `null`. |

### Mappers

| Mapper | Strategy | Direction |
|---|---|---|
| `HistoryMessageDtoMapper` | tool_interaction | `FromDtoList` — expands `tool_interaction` items into paired FunctionCall/FunctionResult with generated ids |
| `PairedCallDtoMapper` | paired-call | `ToDto` + `FromDto` — bidirectional mapping preserving id/call_id correlation |

---

## Usage example

```csharp
// Strategy 1: tool_interaction (recommended default)
var reducer = new ChatHistoryStructuredReducer(
    chatCompletionService,
    retainedMessageCount: 20,
    thresholdCount: 10)
{
    FailOnError = false
};

// Strategy 2: paired function_call / function_result
var reducer = new ChatHistoryPairedCallReducer(
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

## Class hierarchy

```
IChatHistoryReducer (Semantic Kernel)
  └─ ChatHistoryLlmReducerBase (abstract — shared orchestration)
       ├─ ChatHistoryStructuredReducer (tool_interaction schema)
       └─ ChatHistoryPairedCallReducer (function_call/function_result schema)
```

---

## Files

| File | Contents |
|---|---|
| `ChatHistoryLlmReducerBase.cs` | Abstract base class — shared LLM orchestration, boundary detection, result assembly |
| `ChatHistoryStructuredReducer.cs` | tool_interaction reducer (schema, prompts, validation, mapping) |
| `ChatHistoryPairedCallReducer.cs` | Paired-call reducer (schema, prompts, normalization, validation, mapping) |
| `HistoryMessageDto.cs` | Shared DTOs: `HistoryMessageDto`, `HistoryContentItemDto`, `AnyValueToStringDictionaryConverter` |
| `HistoryMessageDtoMapper.cs` | Mapper for tool_interaction strategy (`FromDtoList`) |
| `PairedCallDtoMapper.cs` | Bidirectional mapper for paired-call strategy (`ToDto` + `FromDto`) |
