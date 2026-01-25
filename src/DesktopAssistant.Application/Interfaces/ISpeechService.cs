namespace DesktopAssistant.Application.Interfaces;

/// <summary>
/// Сервис распознавания речи (Speech-to-Text)
/// </summary>
public interface ISpeechRecognitionService
{
    /// <summary>
    /// Событие при распознавании частичного результата (в процессе речи)
    /// </summary>
    event EventHandler<SpeechRecognitionEventArgs>? PartialResultReceived;

    /// <summary>
    /// Событие при распознавании финального результата
    /// </summary>
    event EventHandler<SpeechRecognitionEventArgs>? FinalResultReceived;

    /// <summary>
    /// Событие при обнаружении wake word
    /// </summary>
    event EventHandler<WakeWordDetectedEventArgs>? WakeWordDetected;

    /// <summary>
    /// Запускает распознавание речи
    /// </summary>
    Task StartRecognitionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает распознавание речи
    /// </summary>
    Task StopRecognitionAsync();

    /// <summary>
    /// Проверяет, активно ли распознавание
    /// </summary>
    bool IsRecognizing { get; }

    /// <summary>
    /// Устанавливает wake words для обнаружения
    /// </summary>
    void SetWakeWords(IEnumerable<string> wakeWords);

    /// <summary>
    /// Включает/выключает режим обнаружения wake word
    /// </summary>
    void SetWakeWordMode(bool enabled);
}

/// <summary>
/// Сервис синтеза речи (Text-to-Speech)
/// </summary>
public interface ITextToSpeechService
{
    /// <summary>
    /// Событие начала воспроизведения
    /// </summary>
    event EventHandler? SpeechStarted;

    /// <summary>
    /// Событие окончания воспроизведения
    /// </summary>
    event EventHandler? SpeechCompleted;

    /// <summary>
    /// Синтезирует и воспроизводит текст
    /// </summary>
    Task SpeakAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Синтезирует текст в режиме стриминга (для LLM ответов)
    /// </summary>
    IAsyncEnumerable<byte[]> StreamSpeechAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Останавливает воспроизведение
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Проверяет, идёт ли воспроизведение
    /// </summary>
    bool IsSpeaking { get; }

    /// <summary>
    /// Устанавливает голос для синтеза
    /// </summary>
    Task SetVoiceAsync(string voiceName);

    /// <summary>
    /// Получает список доступных голосов
    /// </summary>
    Task<IEnumerable<VoiceInfo>> GetAvailableVoicesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Информация о голосе
/// </summary>
public record VoiceInfo(string Name, string DisplayName, string Language, string Gender);

/// <summary>
/// Аргументы события распознавания речи
/// </summary>
public class SpeechRecognitionEventArgs : EventArgs
{
    public string Text { get; }
    public bool IsFinal { get; }
    public double Confidence { get; }

    public SpeechRecognitionEventArgs(string text, bool isFinal, double confidence = 1.0)
    {
        Text = text;
        IsFinal = isFinal;
        Confidence = confidence;
    }
}

/// <summary>
/// Аргументы события обнаружения wake word
/// </summary>
public class WakeWordDetectedEventArgs : EventArgs
{
    public string WakeWord { get; }
    public DateTime DetectedAt { get; }

    public WakeWordDetectedEventArgs(string wakeWord)
    {
        WakeWord = wakeWord;
        DetectedAt = DateTime.UtcNow;
    }
}
