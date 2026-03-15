using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Aggregation;

/// <summary>
/// Aggregates streaming message fragments into a complete ChatMessageContent,
/// with support for function calls, files, and other content types.
/// </summary>
public class StreamingChatMessageAggregator
{
    private readonly StringBuilder _contentBuilder = new();
    private readonly FunctionCallContentBuilder _functionCallBuilder = new();
    private readonly Dictionary<string, object?> _metadata = new();
#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.
    private readonly List<FileReferenceContent> _fileReferences = new();
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.

    private AuthorRole? _role;
    private string? _authorName;
    private string? _modelId;
    private Encoding _encoding = Encoding.UTF8;

    public void Append(StreamingChatMessageContent streamingContent)
    {
        // 1. Accumulate basic properties
        _role ??= streamingContent.Role;
#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.
        _authorName ??= streamingContent.AuthorName;
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.
        _modelId ??= streamingContent.ModelId;
        _encoding = streamingContent.Encoding;

        // 2. Accumulate text
        if (streamingContent.Content is not null)
        {
            _contentBuilder.Append(streamingContent.Content);
        }

        // 3. Accumulate function calls
        _functionCallBuilder.Append(streamingContent);

        // 4. Accumulate metadata
        if (streamingContent.Metadata != null)
        {
            foreach (var kvp in streamingContent.Metadata)
            {
                _metadata[kvp.Key] = kvp.Value;
            }
        }

        // 5. Process the Items collection for other content types
        foreach (var item in streamingContent.Items)
        {
#pragma warning disable SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.
            switch (item)
            {
                // Text content already handled above via Content property
                case StreamingTextContent:
                    break;

                // Function call updates handled via FunctionCallContentBuilder
                case StreamingFunctionCallUpdateContent:
                    break;

                // CORRECT CONVERSION: create a new FileReferenceContent from StreamingFileReferenceContent
                case StreamingFileReferenceContent streamingFileRef:
                    _fileReferences.Add(new FileReferenceContent(streamingFileRef.FileId));
                    break;

                    // Additional streaming types can be handled here
                    // case StreamingAnnotationContent streamingAnnotation:
                    //     _annotations.Add(new AnnotationContent(...));
                    //     break;
            }
#pragma warning restore SKEXP0110 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.
        }
    }

    public ChatMessageContent Build()
    {
        var finalContent = _contentBuilder.ToString();
        var functionCalls = _functionCallBuilder.Build();

#pragma warning disable SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.
        var chatMessageContent = new ChatMessageContent(
            role: _role ?? AuthorRole.Assistant,
            content: finalContent,
            modelId: _modelId,
            encoding: _encoding,
            metadata: _metadata
        )
        {
            AuthorName = _authorName
        };
#pragma warning restore SKEXP0001 // Type is for evaluation purposes only and is subject to change or removal in future updates. Suppress this diagnostic to continue.

        // Add function calls
        foreach (var functionCall in functionCalls)
        {
            chatMessageContent.Items.Add(functionCall);
        }

        // Add file references
        foreach (var fileRef in _fileReferences)
        {
            chatMessageContent.Items.Add(fileRef);
        }

        return chatMessageContent;
    }
}
