using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesktopAssistant.Application.Interfaces;

public interface IConversationSessionService : IDisposable
{
    /// <summary>
    /// Возвращает существующую сессию или создаёт новую.
    /// </summary>
    /// <param name="conversationId">id диалога</param>
    /// <returns></returns>
    Task<IConversationSession> GetOrCreate(Guid conversationId);

    /// <summary>
    /// Освобождает сессию (при удалении диалога).
    /// </summary>
    void Release(Guid conversationId);
}

