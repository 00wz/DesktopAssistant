# ChatHistoryCompactionReducer

Two implementations of `IChatHistoryReducer` (Semantic Kernel) that compact conversation history
by asking an LLM to call a single structured tool — `submit_history` — with the reduced message list.

Unlike the built-in `ChatHistorySummarizationReducer`, which replaces a range of messages with a
single free-text summary, these reducers produce a proper list of `ChatMessageContent` objects,
preserving roles, function calls, and function results. That makes them compatible with agents that
use `FunctionChoiceBehavior.Required`, where function call / result pairs must always appear
together in the history.

Both reducers share a common base class (`ChatHistoryCompactionReducerBase`) that handles all
orchestration (boundary detection, LLM invocation, JSON parsing, result assembly) and differ only
in their serialization schema, system prompts, validation, and mapping.

---

## Two schema variants

Both variants share the same class name `ChatHistoryCompactionReducer` and `HistoryDtoMapper`,
separated by namespace. This makes it easy to select a winner for a future Semantic Kernel PR —
just promote one namespace to the root.

### 1. ToolInteractionSchema (`...Summarization.ToolInteractionSchema`)

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

### 2. PairedCallSchema (`...Summarization.PairedCallSchema`)

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

## How it works (shared logic in ChatHistoryCompactionReducerBase)

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
- System message: `ReduceSystemPrompt` — hard constraints specific to the chosen schema
- The messages in the compaction range (index `firstNonSystemIndex` through `truncationIndex`, inclusive)
- User message: `ReduceUserMessage` — the compaction task description (shared between variants)

The LLM is called with `FunctionChoiceBehavior.Required([submit_history], autoInvoke: false)`, so
it must respond by calling `submit_history` exactly once. The function is never auto-invoked;
its arguments are read directly from the response.

**Step 3 — Parse, validate, assemble**

The `messages` argument is deserialized to `List<HistoryMessageDto>` and passed to the
variant-specific `ValidateDtos` and `MapToMessages` methods. The final history is assembled as:
```
[original system message] + [compacted messages] + [retained tail]
```

---

## Constructor (same API for both variants)

```csharp
// ToolInteractionSchema variant
using DesktopAssistant.Infrastructure.AI.Summarization.ToolInteractionSchema;
var reducer = new ChatHistoryCompactionReducer(service, retainedMessageCount: 20, thresholdCount: 10);

// PairedCallSchema variant
using DesktopAssistant.Infrastructure.AI.Summarization.PairedCallSchema;
var reducer = new ChatHistoryCompactionReducer(service, retainedMessageCount: 20, thresholdCount: 10);
```

| Parameter | Description |
|---|---|
| `service` | Chat completion service used for the compaction call. |
| `retainedMessageCount` | Number of messages at the tail to keep verbatim. Everything to the left of this boundary is sent for compaction. `0` (default) = compact everything (subject to pair-safety). Must be non-negative. |
| `thresholdCount` | Best-effort hysteresis: reduction is skipped unless `chatHistory.Count - retainedMessageCount >= thresholdCount`. Note that because the LLM is not guaranteed to reduce message count, this guard may not prevent repeated calls. |

---

## Properties (inherited from ChatHistoryCompactionReducerBase)

| Property | Default | Description |
|---|---|---|
| `ReduceSystemPrompt` | Variant-specific | System message establishing hard constraints. Each variant defines its own default. |
| `ReduceUserMessage` | Built-in (shared) | User message appended after the history that describes the compaction task. Shared between variants because it does not reference a specific schema. |
| `FailOnError` | `true` | When `true`, exceptions during reduction propagate to the caller. When `false`, `null` is returned on failure (SK interprets `null` as "no reduction performed"). |

---

## DTO layer

Messages are exchanged with the LLM as a flat JSON array described by a JSON Schema embedded in
the `submit_history` function definition. The schema is intentionally `$ref`-free and `oneOf`-free
for compatibility with both OpenAI strict mode and Anthropic.

| DTO | Purpose |
|---|---|
| `HistoryMessageDto` | One message: `role` + `items` |
| `HistoryContentItemDto` | One content block: `type` discriminator + type-specific fields. Contains the union of all fields used by both variants. Unused fields are `null`. |

### Mappers (both named `HistoryDtoMapper`, in different namespaces)

| Namespace | Direction | Notes |
|---|---|---|
| `ToolInteractionSchema` | `FromDtoList` only | Expands `tool_interaction` items into paired FunctionCall/FunctionResult with generated ids |
| `PairedCallSchema` | `ToDto` + `FromDto` | Bidirectional mapping preserving id/call_id correlation |

---

## Class hierarchy

```
IChatHistoryReducer (Semantic Kernel)
  └─ ChatHistoryCompactionReducerBase           ...Summarization
       ├─ ChatHistoryCompactionReducer          ...Summarization.ToolInteractionSchema
       └─ ChatHistoryCompactionReducer          ...Summarization.PairedCallSchema
```

---

## Files

```
Summarization/
├── ChatHistoryCompactionReducerBase.cs     Shared orchestration (abstract base)
├── HistoryMessageDto.cs                    Shared DTOs + JSON converter
├── README.md                               This file
├── ToolInteractionSchema/
│   ├── ChatHistoryCompactionReducer.cs     tool_interaction variant
│   └── HistoryDtoMapper.cs                 Mapper (FromDtoList with expansion)
└── PairedCallSchema/
    ├── ChatHistoryCompactionReducer.cs     function_call/function_result variant
    └── HistoryDtoMapper.cs                 Bidirectional mapper (ToDto + FromDto)
```
