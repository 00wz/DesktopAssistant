using System.Text;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace DesktopAssistant.Infrastructure.AI.Aggregation;

/// <summary>
/// Агрегирует потоковые фрагменты сообщений в полное ChatMessageContent
/// с поддержкой function calls, файлов и других типов контента
/// </summary>
public class StreamingChatMessageAggregator
{
    private readonly StringBuilder _contentBuilder = new();
    private readonly FunctionCallContentBuilder _functionCallBuilder = new();
    private readonly Dictionary<string, object?> _metadata = new();
#pragma warning disable SKEXP0110 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
    private readonly List<FileReferenceContent> _fileReferences = new();
#pragma warning restore SKEXP0110 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.

    private AuthorRole? _role;
    private string? _authorName;
    private string? _modelId;
    private Encoding _encoding = Encoding.UTF8;

    public void Append(StreamingChatMessageContent streamingContent)
    {
        // 1. Аккумулируем базовые свойства
        _role ??= streamingContent.Role;
#pragma warning disable SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
        _authorName ??= streamingContent.AuthorName;
#pragma warning restore SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
        _modelId ??= streamingContent.ModelId;
        _encoding = streamingContent.Encoding;

        // 2. Аккумулируем текст
        if (streamingContent.Content is not null)
        {
            _contentBuilder.Append(streamingContent.Content);
        }

        // 3. Аккумулируем function calls
        _functionCallBuilder.Append(streamingContent);

        // 4. Аккумулируем metadata
        if (streamingContent.Metadata != null)
        {
            foreach (var kvp in streamingContent.Metadata)
            {
                _metadata[kvp.Key] = kvp.Value;
            }
        }

        // 5. Обрабатываем Items коллекцию для других типов контента
        foreach (var item in streamingContent.Items)
        {
#pragma warning disable SKEXP0110 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
            switch (item)
            {
                // Текстовый контент уже обработан выше через Content property
                case StreamingTextContent:
                    break;

                // Function call updates обработаны через FunctionCallContentBuilder
                case StreamingFunctionCallUpdateContent:
                    break;

                // ПРАВИЛЬНАЯ КОНВЕРТАЦИЯ: создаем новый FileReferenceContent из StreamingFileReferenceContent
                case StreamingFileReferenceContent streamingFileRef:
                    _fileReferences.Add(new FileReferenceContent(streamingFileRef.FileId));
                    break;

                    // Можно добавить обработку других streaming типов
                    // case StreamingAnnotationContent streamingAnnotation:
                    //     _annotations.Add(new AnnotationContent(...));
                    //     break;
            }
#pragma warning restore SKEXP0110 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
        }
    }

    public ChatMessageContent Build()
    {
        var finalContent = _contentBuilder.ToString();
        var functionCalls = _functionCallBuilder.Build();

#pragma warning disable SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
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
#pragma warning restore SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.

        // Добавляем function calls
        foreach (var functionCall in functionCalls)
        {
            chatMessageContent.Items.Add(functionCall);
        }

        // Добавляем файловые ссылки
        foreach (var fileRef in _fileReferences)
        {
            chatMessageContent.Items.Add(fileRef);
        }

        return chatMessageContent;
    }
}
