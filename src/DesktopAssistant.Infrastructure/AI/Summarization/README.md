# ChatHistoryCompactionReducer

Implementation of `IChatHistoryReducer` (Semantic Kernel) that compacts conversation history by
asking an LLM to call a single structured tool — `submit_history` — with the reduced message list.

Unlike classic summarization reducers that replace history with one free-text message,
`ChatHistoryCompactionReducer` preserves a structured list of `ChatMessageContent` objects.

---

## Key idea

The reducer uses a `tool_interaction` schema:
- message roles: `user`, `assistant`
- item types: `text`, `tool_interaction`

Each `tool_interaction` item contains both function call data and function result data.
Then `HistoryDtoMapper` expands that item back into native Semantic Kernel sequence:
assistant `FunctionCallContent` + tool `FunctionResultContent` with generated matching IDs.

This significantly lowers structured-output errors compared to split `function_call` / `function_result` formats.

---

## Flow

1. Find safe compaction boundary (`LocateSafeReductionIndex`) so call/result pairs are not broken.
2. Send compactable history to LLM with:
   - system prompt (`ReduceSystemPrompt`)
   - compaction task prompt (`ReduceUserMessage`)
   - required function call to `submit_history`
3. Parse `messages` JSON argument into `List<HistoryMessageDto>`.
4. Validate structural constraints for `tool_interaction` usage.
5. Map DTO list to `List<ChatMessageContent>` via `HistoryDtoMapper`.
6. Assemble final history: `[system] + [compacted] + [retained tail]`.

---

## Known failure points

These issues are not currently handled. Each describes a condition the LLM may produce
and a potential fixup strategy if it becomes necessary to address it.

### 1. `tool_interaction` in a non-assistant message

The LLM may place a `tool_interaction` item inside a `user` message. `Validate` catches
this and throws, so the reduction fails.

Possible fixup strategies (in `ReduceAsync`, after deserialization, before `Validate`):
- Create a new `assistant` message containing the offending `tool_interaction` items and
  insert it immediately after the `user` message.
- Locate the nearest following `assistant` message and move the `tool_interaction` items
  into it.

### 2. Consecutive messages with the same role

The LLM may return two or more messages of the same role in a row. Models sensitive to
role alternation (Gemini, Mistral) will reject such a history.

The `assistant` + `assistant` case is already handled: `HistoryDtoMapper.FromDtoList`
calls `MergeConsecutiveAssistantMessages` after expanding `tool_interaction` items, so
consecutive assistant messages are merged before the result is returned.

The remaining uncovered case is `user` + `user`. Possible fixup (after deserialization,
before mapping): merge consecutive `user` messages by concatenating their `items` lists
into the first message of the run.

### 3. Compacted history does not end on an assistant-only message

`LocateSafeReductionIndex` guarantees that the message immediately after the compaction
boundary is a `user` message. If the LLM's last compacted message is not an `assistant`
message without `tool_interaction`, the assembled history will have a non-assistant message
immediately before a `user` message, breaking role alternation.

Possible fixup (after mapping, before `AssembleResult`): if the last message in the
compacted list is not `assistant` or is `assistant` with `FunctionCallContent` items,
append an empty placeholder `ChatMessageContent(AuthorRole.Assistant)`.

---

## Public API

```csharp
var reducer = new ChatHistoryCompactionReducer(
    chatCompletionService,
    retainedMessageCount: 20,
    thresholdCount: 10)
{
    FailOnError = false
};

var reduced = await reducer.ReduceAsync(chatHistory, cancellationToken);
```

- `reduced == null`: reduction skipped or failed (when `FailOnError=false`)
- `reduced != null`: use reduced history

---

## Files

- `ChatHistoryCompactionReducer.cs` — main reducer
- `HistoryDtoMapper.cs` — DTO → SK mapping with `tool_interaction` expansion
- `HistoryMessageDto.cs` — DTO model and JSON converter for flexible argument values
