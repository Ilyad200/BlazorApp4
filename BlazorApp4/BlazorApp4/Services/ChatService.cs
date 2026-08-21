using BlazorApp4.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using OpenRouter.NET.Models;

namespace BlazorApp4.Services
{
    public class ChatService
    {
        public List<ChatDb> DbChats { get; private set; } = new();
        public ChatDb? CurrentChatDb { get; private set; }
        public ChatDb? CompareWithChat { get; private set; }
        public int? CurrentChatDbId { get; private set; }
        public int? CompareWithChatId { get; private set; }


        public int? CompareStartOrderMain { get; private set; }
        public int? CompareStartOrderCompare { get; private set; }
        public bool IsSelectingCompareStart { get; private set; }
        public bool IsCompareAligned => CompareStartOrderMain != null && CompareStartOrderCompare != null;


        private Dictionary<int, int> ContextShiftCacheMain = new(); // <messageId, countEdited>
        private Dictionary<int, int> ContextShiftCacheCompare = new();

        private Dictionary<int, int> VersionsCasheMain = new();
        private Dictionary<int, int> VersionsCasheCompare = new();

        public ChatComparison? CurrentComparison { get; private set; }
        public MessageDb? LeftMessageToStartCompare { get; private set; }
        public MessageDb? RightMessageToStartCompare { get; private set; }

        public event Action<bool>? OnComparisonChanged;
        public event Action OnCompareStart;
        public event Action? OnChange; // для оповещения UI о любых изменениях
        private void NotifyStateChanged()
        {
            if (OnChange == null)
                return;

            foreach (var handler in OnChange.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception ex)
                {
                    Console.BackgroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Handler: {handler.Method.DeclaringType}.{handler.Method.Name}");
                    Console.WriteLine(ex);
                    Console.ResetColor();
                    throw;
                }
            }
        }

        //private readonly ApplicationDbContext _context;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public ChatService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
            using var context = _contextFactory.CreateDbContext();
            DbChats = context.Chats.Include(c => c.Messages).ToList();
        }

        private async Task RefreshDbChatsAsync()
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            DbChats = context.Chats.Include(c => c.Messages).ToList();
        }

        public void BeginSelectingCompareStart()
        {
            IsSelectingCompareStart = true;
            CompareStartOrderMain = null;
            CompareStartOrderCompare = null;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"IsSelectingCompareStart: {IsSelectingCompareStart}");
            Console.ResetColor();
            NotifyStateChanged();
        }

        public void CancelSelectingCompareStart()
        {
            IsSelectingCompareStart = false;
            CompareStartOrderMain = null;
            CompareStartOrderCompare = null;
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"IsSelectingCompareStart: {IsSelectingCompareStart}");
            Console.ResetColor();
            NotifyStateChanged();
        }

        // isCompare указывает, в какой из двух панелей (основной / сравнения)
        // пользователь кликнул по сообщению.
        public void SetCompareStart(bool isCompare, int order)
        {
            if (!IsSelectingCompareStart) return;

            if (isCompare) CompareStartOrderCompare = order;
            else CompareStartOrderMain = order;

            if (IsCompareAligned)
            {
                IsSelectingCompareStart = false;
                OnCompareStart.Invoke();
            }
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"CompareStartOrderCompare: {CompareStartOrderCompare}");
            Console.WriteLine($"CompareStartOrderMain: {CompareStartOrderMain}");
            Console.ResetColor();
            NotifyStateChanged();
        }

        public void ResetCompareAlignment()
        {
            CompareStartOrderMain = null;
            CompareStartOrderCompare = null;
            IsSelectingCompareStart = false;
            NotifyStateChanged();
        }
        // Управление ветками
        public async Task AddChatAsync(string? name = null)
        {
            ResetCompareAlignment();
            CompareWithChatId = null;
            CompareWithChat = null;
            using var context = _contextFactory.CreateDbContext();

            var chat = new ChatDb(name ?? $"Chat {DbChats.Count + 1}");
            await context.Chats.AddAsync(chat);
            await context.SaveChangesAsync();
            DbChats.Add(chat);
            Console.WriteLine("///////////////////////////////////////////////////////// " + chat.Id);

            await RefreshDbChatsAsync(); 
            CurrentChatDb = chat;
            CurrentChatDbId = chat.Id;
            //ContextShiftCache.Add(chat.Messages[0].Id, 0);
            NotifyStateChanged();
        }
        public async Task DeleteChatAsync(int chatId)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var chat = await context.Chats.FindAsync(chatId);
            if (chat == null) return;
            context.Chats.Remove(chat);
            await context.SaveChangesAsync();

            await RefreshDbChatsAsync();

            if (CurrentChatDbId == chatId)
            {
                CurrentChatDb = null;
                CurrentChatDbId = null;
            }
            if (CompareWithChatId == chatId)
            {
                CompareWithChat = null;
                CompareWithChatId = null;
            }

            NotifyStateChanged();
        }

        public async Task SwitchChat(int chatId)
        {
            CurrentChatDbId = chatId;
            CurrentChatDb = await GetChatById(chatId);
            await RebuildContextShiftCacheAsync(chatId);
            NotifyStateChanged();
        }

        public List<ChatDb> GetAllChats()
        {
            using var context = _contextFactory.CreateDbContext();
            var chats = context.Chats.Include(c => c.Messages).ToList();
            return chats;
        }

        private void GetCurrentVersionsChat(bool isCompare)
        {
            VersionsCasheMain.Clear();
            VersionsCasheCompare.Clear();
            if (isCompare)
            {
                if (CurrentChatDb == null) return;
                foreach (var m in CurrentChatDb.Messages)
                {
                    VersionsCasheMain[m.Id] = m.CurrentVersionOrder;
                }
            }
            else
            {
                if (CompareWithChat == null) return;
                foreach (var m in CompareWithChat.Messages)
                {
                    VersionsCasheCompare[m.Id] = m.CurrentVersionOrder;
                }
            }
        }
        public async Task<ChatDb> GetChatById(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            //var chat = _context.Chats.FirstOrDefault(c => c.Id == id);
            var chat = await context.Chats.Include(c => c.Messages).ThenInclude(m => m.Versions).FirstOrDefaultAsync(c => c.Id == id);
            return chat ?? throw new Exception("Chat not found");
        }
        private async Task RefreshChatSlot(int chatId, bool isCompare = false)
        {
            var chat = await GetChatById(chatId);

            
            if (CompareWithChat != null && CompareWithChat.Id == chatId)
            {
                if (CompareWithChatId == CurrentChatDbId)
                {
                    CompareWithChat = new(chat);
                }
                else
                {
                    CompareWithChat = chat;
                }
                CompareWithChatId = chatId;
            }

            // Если это не чат сравнения (или основной чат ещё не выбран) — считаем основным.
            if (CurrentChatDb != null && CurrentChatDb.Id == chatId)
            {
                CurrentChatDb = chat;
                CurrentChatDbId = chatId;
            }
        }
        public async Task AddMessageAsync(int chatId, string role, string content, string? model, bool isOriginal = false)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"chatId {chatId}; role {role}; content {content}; model {model}; isOriginal {isOriginal};");

            using var context = _contextFactory.CreateDbContext();
            var chat = await context.Chats
                .Include(c => c.Messages).ThenInclude(m => m.Versions)
                .FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null) throw new Exception("Chat not found");

            var msg = new MessageDb { Role = role, Order = chat.Messages.Count };
            Console.WriteLine(role);
            Console.WriteLine(content);
            Console.ResetColor();

            //var previousVersions = role == "assistant" ? await GetPreviousVersions(chatId) : null;
            var newVersion = msg.AddVersion(content, model, isOriginal);
            if (isOriginal)
            {
                ChatSnapshot? snapshot = await SetSnapshotAsync(context, chatId, msg.Order);
                if (snapshot != null)
                {
                    context.ChatSnapshots.Add(snapshot);
                    newVersion.Snapshot = snapshot;
                }
            }
            chat.Messages.Add(msg);
            await context.SaveChangesAsync();

            await RefreshChatSlot(chatId);
            await UpdateContextShiftAfterEditAsync(chatId, msg.Order);
            await RefreshDbChatsAsync();
            NotifyStateChanged();
        }
        public async Task<ChatSnapshot?> SetSnapshotAsync(ApplicationDbContext context, int chatId, int beforeOrder)
        {
            var chat = await context.Chats
                        .Include(c => c.Messages)
                            .ThenInclude(m => m.Versions)
                            .FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null)
            {
                //throw new Exception($"[ChatService SetSnapshotAsync] Chat not found");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ChatService SetSnapshotAsync] Chat not found");
                Console.ResetColor();
                return null;
            }
            ChatSnapshot snapshot = new ChatSnapshot
            {
                ChatId = chatId,
                CreatedAt = DateTimeOffset.UtcNow
            };
            foreach (var msg in chat.Messages.OrderBy(m => m.Order))
            {
                if (msg.Order >= beforeOrder) break;
                // Для каждого сообщения сохраняем его текущую версию (последнюю активную)
                snapshot.Entries.Add(new SnapshotEntry
                {
                    MessageId = msg.Id,
                    VersionId = msg.GetCurrentVersion().Id
                });
            }
            return snapshot;
        }
        public async Task<List<VersionDb>> GetPreviousVersions(int chatId, int? messageOrder = null)
        {
            using var context = _contextFactory.CreateDbContext();
            ChatDb? chat = await context.Chats.Include(c => c.Messages).ThenInclude(m => m.Versions).FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ChatService GetPreviousVersions]: chat is null; id: {chatId}");
                Console.ResetColor();
                return new();
            }
            if (messageOrder == null)
            {
                messageOrder = chat.Messages.Count - 1;
            }
            List<VersionDb> previousVersions = new();
            Console.ForegroundColor = ConsoleColor.Green;
            for (int i = 0; i < messageOrder; i++)
            {
                var vers = chat.Messages[i].GetCurrentVersion();
                Console.WriteLine($"[ChatService GetPreviousVersions]: {i}");
                Console.WriteLine($"[ChatService GetPreviousVersions]: {vers.Content}");
                previousVersions.Add(vers);
            }
            Console.ResetColor();
            return previousVersions;
        }
        public async Task UpdateMessageAsync(int messageId, string newContent, string newModel, bool isOriginal = false, bool isCompare = false)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            // Загружаем сообщение вместе с существующими версиями
            var message = await context.Messages
                .Include(m => m.Versions)
                .FirstOrDefaultAsync(m => m.Id == messageId);

            if (message == null) throw new Exception("Сообщение не найдено");

            GetCurrentVersionsChat(isCompare);
            //Console.WriteLine($"--------- CurrentChatDb {CurrentChatDb.Messages[1].CurrentVersionOrder}");
            //Console.WriteLine($"--------- CompareWithChat {CompareWithChat.Messages[1].CurrentVersionOrder}");
            //if (VersionsCasheMain.Count > 0)
            //Console.WriteLine($"--------- VersionsCasheMain {VersionsCasheMain[CurrentChatDb.Messages[1].Id]}");
            //if (VersionsCasheCompare.Count > 0)
            //Console.WriteLine($"--------- VersionsCasheCompare {VersionsCasheCompare[CompareWithChat.Messages[1].Id]}");
            // Добавляем новую версию – метод сам обновит CurrentVersion
            var newVersion = message.AddVersion(newContent, newModel, isOriginal);

            if (isOriginal)
            {
                var snapshot = await SetSnapshotAsync(context, message.ChatId, message.Order);
                if (snapshot != null)
                {
                    context.ChatSnapshots.Add(snapshot);
                    newVersion.Snapshot = snapshot;
                }
            }

            // Сохраняем изменения – вставляется новая версия и обновляется CurrentVersionId
            await context.SaveChangesAsync();

            await RefreshChatSlot(message.ChatId, isCompare);

            //Console.WriteLine($"+++++++++ CurrentChatDb {CurrentChatDb.Messages[1].CurrentVersionOrder}");
            //Console.WriteLine($"+++++++++ CompareWithChat {CompareWithChat.Messages[1].CurrentVersionOrder}");
            //if (VersionsCasheMain.Count > 0)
            //    Console.WriteLine($"+++++++++ VersionsCasheMain {VersionsCasheMain[CurrentChatDb.Messages[1].Id]}");
            //if (VersionsCasheCompare.Count > 0)
            //    Console.WriteLine($"+++++++++ VersionsCasheCompare {VersionsCasheCompare[CompareWithChat.Messages[1].Id]}");

            if (CompareWithChatId == CurrentChatDbId)
            {
                if (isCompare && CurrentChatDb != null)
                {
                    foreach (var v in VersionsCasheMain)
                    {
                        CurrentChatDb.Messages.First(m => m.Id == v.Key).CurrentVersionOrder = v.Value;
                    }
                }
                if (!isCompare && CompareWithChat != null)
                {
                    foreach (var v in VersionsCasheCompare)
                    {
                        CompareWithChat.Messages.First(m => m.Id == v.Key).CurrentVersionOrder = v.Value;
                    }
                }
            }
    //        Console.WriteLine($"///////// CurrentChatDb {CurrentChatDb.Messages[1].CurrentVersionOrder}");
    //        Console.WriteLine($"///////// CompareWithChat {CompareWithChat.Messages[1].CurrentVersionOrder}");
    //        if (VersionsCasheMain.Count > 1)
    //            Console.WriteLine($"///////// VersionsCasheMain {VersionsCasheMain[CurrentChatDb.Messages[1].Id]}");
    //        if (VersionsCasheCompare.Count > 1)
    //            Console.WriteLine($"///////// VersionsCasheCompare {VersionsCasheCompare[CompareWithChat.Messages[1].Id]}");
    //        Console.WriteLine(
    //ReferenceEquals(CurrentChatDb.Messages[1],
    //                CompareWithChat.Messages[1]));
            await UpdateContextShiftAfterEditAsync(message.ChatId, message.Order, isCompare);
            await RefreshDbChatsAsync();
            NotifyStateChanged();
        }
        public async Task MakeOriginal(int messageId, int versionId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            var message = context.Messages.Include(m => m.Versions).FirstOrDefault(m => m.Id == messageId);
            if (message == null) return;
            var version = message.Versions.FirstOrDefault(v => v.Id == versionId);
            if (version == null) return;
            version.IsOriginal = true;

            var snapshot = await SetSnapshotAsync(context, message.ChatId, message.Order);
            if (snapshot != null)
            {
                context.ChatSnapshots.Add(snapshot);
                version.Snapshot = snapshot;
            }

            await context.SaveChangesAsync();
            NotifyStateChanged();
        }


        // Переключиться на предыдущую версию сообщения
        public async Task SwitchToVersionAsync(int chatId, int messageId, int versionId, bool isCompare = false)
        {
            if (CompareWithChatId == CurrentChatDbId && CompareWithChatId == chatId)
            {
                MessageDb? msg = null;
                if (isCompare)
                {
                    if (CompareWithChat == null) return;
                    msg = CompareWithChat.Messages.FirstOrDefault(m => m.Id == messageId);
                }
                else
                {
                    if (CurrentChatDb == null) return;
                    msg = CurrentChatDb.Messages.FirstOrDefault(m => m.Id == messageId);
                }
                if (msg == null) return;
                var vers = msg.Versions.FirstOrDefault(v => v.Id == versionId);
                if (vers == null) return;
                msg.CurrentVersionOrder = vers.Order;

                await UpdateContextShiftAfterEditAsync(chatId, msg.Order, isCompare);
            }
            else
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var msg = context.Messages
                            .Include(m => m.Versions)
                            .FirstOrDefault(m => m.Id == messageId);
                if (msg == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ChatService SwitchToVersionAsync] msg is null (id: {messageId})");
                    Console.ResetColor();
                    return;
                }

                var vers = msg!.Versions.FirstOrDefault(v => v.Id == versionId);
                if (vers == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ChatService SwitchToVersionAsync] vers is null (id: {versionId}, msg Id: {msg.Id})");
                    Console.ResetColor();
                    return;
                }

                msg.CurrentVersionOrder = vers.Order;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ChatService SwitchToVersionAsync] CurrentVersionOrder is {msg.CurrentVersionOrder}");
                Console.ResetColor();
                await context.SaveChangesAsync();
                await RefreshChatSlot(chatId);
                await UpdateContextShiftAfterEditAsync(chatId, msg.Order, isCompare);
                await RefreshDbChatsAsync();
            }
            NotifyStateChanged();
        }
        public async Task SwitchToNextVersionAsync(int messageId)
        {
            using var context = _contextFactory.CreateDbContext();
            var msg = context.Messages
                        .Include(m => m.Versions)
                        .FirstOrDefault(c => c.Id == messageId);
            if (msg != null && msg.CurrentVersionOrder < msg.Versions.Count - 1)
            {
                //msg.CurrentVersion = msg.Versions[(int)msg.CurrentVersionOrder + 1];
                msg.CurrentVersionOrder++;
                await context.SaveChangesAsync();
            }
        }

        public async Task PreviousVersions(int chatId, VersionDb lastVersion)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("ChatService SetPreviousVersions: start");
            Console.ResetColor();
            using var context = await _contextFactory.CreateDbContextAsync();
            ChatDb? chat = await context.Chats.Include(c => c.Messages)
                            .ThenInclude(m => m.Versions)
                            .FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ChatService PreviousVersions] Chat is null");
                Console.ResetColor();
                return;
            }

            ChatSnapshot? snapshot = await context.ChatSnapshots
                                        .Include(s => s.Entries)
                                        .FirstOrDefaultAsync(s => s.Id == lastVersion.SnapshotId);
            if (snapshot == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ChatService PreviousVersions] Snapshot is null (id: {lastVersion.SnapshotId}, versionId: {lastVersion.Id})");
                Console.ResetColor();
                return;
            }
            if (snapshot!.Entries == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ChatService PreviousVersions] snapshot.Entries is null");
                Console.ResetColor();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[ChatService PreviousVersions] snapshot Entries count: {snapshot!.Entries.Count}");
            Console.WriteLine($"[ChatService PreviousVersions] snapshot Id: {snapshot.Id}");
            Console.ResetColor();
            //var messages = chat.Messages;
            foreach (var entry in snapshot.Entries!)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[ChatService PreviousVersions] entry id: {entry.Id}; messageId: {entry.MessageId}");
                Console.ResetColor();
                await SwitchToVersionAsync(chatId, entry.MessageId, entry.VersionId);
            }
            Console.ResetColor();
            NotifyStateChanged();
        }
        private async Task<ChatDb?> GetFullChat(int chatId, bool isCompare = false)
        {
            if (CurrentChatDbId == CompareWithChatId && CurrentChatDbId == chatId)
            {
                if (isCompare) return CompareWithChat;
            }
            await using var context = await _contextFactory.CreateDbContextAsync();

            var chat = await context.Chats
                .Include(c => c.Messages)
                    .ThenInclude(m => m.Versions)
                        .ThenInclude(v => v.Snapshot)
                            .ThenInclude(s => s.Entries)
                .FirstOrDefaultAsync(c => c.Id == chatId);
            return chat;
        }
        private async Task UpdateContextShiftAfterEditAsync(int chatId, int editedMsgOrder, bool isCompare = false)
        {
            var chat = await GetFullChat(chatId, isCompare);
            if (chat == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ChatService CalculateContextShift] chat is null; id: {chatId}");
                Console.ResetColor();
                return;
            }
            var lookup = chat.Messages.ToDictionary(m => m.Id);
            foreach (var m in chat.Messages.OrderBy(m => m.Order))
            {
                if (m.Order < editedMsgOrder) continue;
                var curVers = m.GetCurrentVersion();
                if (curVers.Snapshot?.Entries == null) continue;
                int count = 0;
                foreach (var entry in curVers.Snapshot!.Entries)
                {
                    if (!lookup.TryGetValue(entry.MessageId, out var message))
                        continue;
                    var currentVersion = message.GetCurrentVersion();
                    if (currentVersion.Id != entry.VersionId)
                    {
                        count++;
                    }
                }
                if (isCompare)
                    ContextShiftCacheCompare[m.Id] = count;
                else
                    ContextShiftCacheMain[m.Id] = count;
                if (isCompare)
                    Console.WriteLine($"[ChatService UpdateContextShiftAfterEditAsync] ContextShiftCacheCompare: {m.Id} {count}");
                else
                    Console.WriteLine($"[ChatService UpdateContextShiftAfterEditAsync] ContextShiftCacheMain: {m.Id} {count}");
            }
        }
        private async Task RebuildContextShiftCacheAsync(int chatId, bool isCompare = false)
        {
            if (isCompare)
                ContextShiftCacheCompare.Clear();
            else
                ContextShiftCacheMain.Clear();

            await using var context =
                await _contextFactory.CreateDbContextAsync();

            var chat = await context.Chats
                .Include(c => c.Messages)
                    .ThenInclude(m => m.Versions)
                        .ThenInclude(v => v.Snapshot)
                            .ThenInclude(s => s.Entries)
                .FirstOrDefaultAsync(c => c.Id == chatId);

            if (chat == null)
                return;

            var lookup = chat.Messages.ToDictionary(m => m.Id);

            foreach (var m in chat.Messages.OrderBy(m => m.Order))
            {
                var curVers = m.GetCurrentVersion();

                if (curVers.Snapshot?.Entries == null)
                {
                    continue;
                }

                int count = 0;

                foreach (var entry in curVers.Snapshot.Entries)
                {
                    if (!lookup.TryGetValue(entry.MessageId, out var message))
                        continue;

                    var currentVersion = message.GetCurrentVersion();

                    if (currentVersion.Id != entry.VersionId)
                        count++;
                }

                if (isCompare)
                    ContextShiftCacheCompare[m.Id] = count;
                else
                    ContextShiftCacheMain[m.Id] = count;
            }
        }
        public int GetContextShift(int msgId, bool isCompare = false)
        {
            if (isCompare)
                return ContextShiftCacheCompare.TryGetValue(msgId, out int valueCompare) ? valueCompare : 0;
            return ContextShiftCacheMain.TryGetValue(msgId, out var value) ? value : 0;
        }

        //public void ToggleComparison(string chatId)
        //{
        //    if (ComparisonChatIds.Contains(chatId))
        //        ComparisonChatIds.Remove(chatId);
        //    else
        //    {
        //        if (ComparisonChatIds.Count >= 2)
        //        {
        //            // Удаляем самый старый (первый добавленный) или запрещаем
        //            ComparisonChatIds.RemoveAt(0);
        //        }
        //        ComparisonChatIds.Add(chatId);
        //    }
        //    OnComparisonChanged?.Invoke();
        //    NotifyStateChanged(); // чтобы обновить боковую панель и другие подписчики
        //}

        public void SetLeftMessageToCompare(MessageDb leftMessage)
        {
            if (RightMessageToStartCompare != null)
            {
                CurrentComparison = new ChatComparison
                {
                    LeftChatId = leftMessage.ChatId,
                    RightChatId = RightMessageToStartCompare.ChatId,
                    LeftStartMessageId = leftMessage.Id,
                    RightStartMessageId = RightMessageToStartCompare.Id,
                };
            }
            else
            {
                LeftMessageToStartCompare = leftMessage;
            }
        }
        public void SetRightMessageToCompare(MessageDb rightMessage)
        {
            if (LeftMessageToStartCompare != null)
            {
                CurrentComparison = new ChatComparison
                {
                    LeftChatId = LeftMessageToStartCompare.ChatId,
                    RightChatId = rightMessage.ChatId,
                    LeftStartMessageId = LeftMessageToStartCompare.Id,
                    RightStartMessageId = rightMessage.Id,
                };
            }
            else
            {
                RightMessageToStartCompare = rightMessage;
            }
        }
        public List<MessageDb> GetMessagesAfterPoint(ChatDb chat, int messageId)
        {
            var startMessage =
                chat.Messages.FirstOrDefault(m => m.Id == messageId);

            if (startMessage == null)
                return new();

            return chat.Messages
                .Where(m => m.Order > startMessage.Order)
                .OrderBy(m => m.Order)
                .ToList();
        }
        public (List<MessageDb> Left, List<MessageDb> Right) GetComparedMessages()
        {
            if (CurrentComparison == null)
                return (new(), new());

            var leftChat =
                DbChats.FirstOrDefault(
                    c => c.Id == CurrentComparison.LeftChatId);

            var rightChat =
                DbChats.FirstOrDefault(
                    c => c.Id == CurrentComparison.RightChatId);

            if (leftChat == null || rightChat == null)
                return (new(), new());

            return
            (
                GetMessagesAfterPoint(
                    leftChat,
                    CurrentComparison.LeftStartMessageId),

                GetMessagesAfterPoint(
                    rightChat,
                    CurrentComparison.RightStartMessageId)
            );
        }
        public List<MessageDiff> BuildDiff()
        {
            var (left, right) = GetComparedMessages();

            var result = new List<MessageDiff>();

            int count = Math.Max(left.Count, right.Count);

            for (int i = 0; i < count; i++)
            {
                var l = i < left.Count ? left[i] : null;
                var r = i < right.Count ? right[i] : null;

                if (l == null || r == null)
                    continue;

                result.Add(new MessageDiff
                {
                    Left = l,
                    Right = r,
                    Similarity = CalculateSimilarity(
                        l.GetCurrentVersion().Content,
                        r.GetCurrentVersion().Content)
                });
            }

            return result;
        }
        private double CalculateSimilarity(
    string a,
    string b)
        {
            var wordsA =
                a.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => x.ToLower())
                 .ToHashSet();

            var wordsB =
                b.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => x.ToLower())
                 .ToHashSet();

            if (wordsA.Count == 0 &&
                wordsB.Count == 0)
                return 100;

            int intersection =
                wordsA.Intersect(wordsB).Count();

            int union =
                wordsA.Union(wordsB).Count();

            return Math.Round(
                intersection * 100.0 / union,
                1);
        }


        public async Task SetCompareWith(int? chatId)
        {
            Console.WriteLine($"ChatServise.SetCompareWith: {chatId}");
            CompareWithChatId = chatId;

            if (chatId == CurrentChatDbId) CompareWithChat = new ChatDb(CurrentChatDb);
            else CompareWithChat = chatId == null ? null : await GetChatById((int)chatId);

            Console.WriteLine($"ChatService chatId == null ? ({chatId == null})");

            if (chatId != null)
            {
                await RebuildContextShiftCacheAsync(chatId.Value, true);
            }
            else
            {
                ResetCompareAlignment();
            }
            OnComparisonChanged?.Invoke(chatId != null);
        }
    }
}
