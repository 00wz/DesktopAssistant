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
    private readonly List<KernelContent> _additionalItems = new();

    private AuthorRole? _role;
    private string? _authorName;
    private string? _modelId;
    private Encoding? _encoding;

    /// <summary>
    /// Добавляет фрагмент стримингового сообщения в агрегатор
    /// </summary>
    public void Append(StreamingChatMessageContent streamingContent)
    {
        ArgumentNullException.ThrowIfNull(streamingContent);

        // 1. Аккумулируем базовые свойства (первый непустой wins)
        _role ??= streamingContent.Role;
#pragma warning disable SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
        _authorName ??= streamingContent.AuthorName;
#pragma warning restore SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
        _modelId ??= streamingContent.ModelId;
        _encoding = streamingContent.Encoding;

        // 2. Аккумулируем текстовый контент
        if (streamingContent.Content is not null)
        {
            _contentBuilder.Append(streamingContent.Content);
        }

        // 3. Аккумулируем function calls через специальный builder
        _functionCallBuilder.Append(streamingContent);

        // 4. Аккумулируем metadata (merge)
        if (streamingContent.Metadata != null)
        {
            foreach (var kvp in streamingContent.Metadata)
            {
                _metadata[kvp.Key] = kvp.Value;
            }
        }

        // 5. Обрабатываем другие типы контента в Items
        foreach (var item in streamingContent.Items)
        {
#pragma warning disable SKEXP0110 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
            switch (item)
            {
                // Текст и function calls уже обработаны выше
                case StreamingTextContent:
                case StreamingFunctionCallUpdateContent:
                    break;

                // Файловые ссылки
                case StreamingFileReferenceContent fileRef:
                    _additionalItems.Add(new FileReferenceContent(fileRef.FileId));
                    break;

                // Любой другой KernelContent сохраняем как есть
                default:
                    if (item is KernelContent kernelContent)
                    {
                        _additionalItems.Add(kernelContent);
                    }
                    break;
            }
#pragma warning restore SKEXP0110 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.
        }
    }

    /// <summary>
    /// Собирает накопленные данные в полное ChatMessageContent
    /// </summary>
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
            metadata: _metadata)
        {
            AuthorName = _authorName
        };
#pragma warning restore SKEXP0001 // Тип предназначен только для оценки и может быть изменен или удален в будущих обновлениях. Чтобы продолжить, скройте эту диагностику.

        // Добавляем function calls в Items
        foreach (var functionCall in functionCalls)
        {
            chatMessageContent.Items.Add(functionCall);
        }

        // Добавляем другие типы контента
        foreach (var item in _additionalItems)
        {
            chatMessageContent.Items.Add(item);
        }

        return chatMessageContent;
    }
}
