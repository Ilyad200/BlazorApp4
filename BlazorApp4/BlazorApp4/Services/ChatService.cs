using BlazorApp4.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using Microsoft.JSInterop;
using OpenRouter.NET.Models;
using System.Collections;

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


        public List<double> messagesDiffs { get; private set; }


        public ChatComparison? CurrentComparison { get; private set; }
        public MessageDb? LeftMessageToStartCompare { get; private set; }
        public MessageDb? RightMessageToStartCompare { get; private set; }

        public event Action<bool>? OnComparisonChanged;
        public event Action OnCompareStart;
        public event Action? OnChange;
        public event Action? OnMessagesDiffsChange;
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
                    throw;
                }
            }
        }

        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly SemanticComparisonService _semanticService;

        public ChatService(IDbContextFactory<ApplicationDbContext> contextFactory, SemanticComparisonService service)
        {
            _contextFactory = contextFactory;
            using var context = _contextFactory.CreateDbContext();
            DbChats = context.Chats.Include(c => c.Messages).ToList();

            messagesDiffs = new List<double>();

            _semanticService = service;
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
            NotifyStateChanged();
        }

        public void CancelSelectingCompareStart()
        {
            IsSelectingCompareStart = false;
            CompareStartOrderMain = null;
            CompareStartOrderCompare = null;
            NotifyStateChanged();
        }

        public async Task SetCompareStart(bool isCompare, int order)
        {
            if (!IsSelectingCompareStart) return;

            if (isCompare) CompareStartOrderCompare = order;
            else CompareStartOrderMain = order;

            if (IsCompareAligned)
            {
                IsSelectingCompareStart = false;
                OnCompareStart.Invoke();
                await GetDiff();
            }
            NotifyStateChanged();
        }

        public void ResetCompareAlignment()
        {
            CompareStartOrderMain = null;
            CompareStartOrderCompare = null;
            IsSelectingCompareStart = false;
            NotifyStateChanged();
        }
        public async Task AddChatAsync(string? name = null)
        {
            ResetCompareAlignment();
            CompareWithChatId = null;
            CompareWithChat = null;
            using var context = _contextFactory.CreateDbContext();

            var chat = new ChatDb(name ?? $"Chat {DbChats.Count + 1}");
            await context.Chats.AddAsync(chat);
            await context.SaveChangesAsync();

            await RefreshDbChatsAsync(); 
            CurrentChatDb = chat;
            CurrentChatDbId = chat.Id;
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

        public async Task RenameChatAsync(int chatId, string newName)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var chat = await context.Chats.FindAsync(chatId);
            if (chat == null) return;

            chat.Name = newName;
            await context.SaveChangesAsync();
            await RefreshDbChatsAsync();
            NotifyStateChanged();
        }

        public async Task SwitchChat(int chatId)
        {
            CurrentChatDbId = chatId;
            CurrentChatDb = await GetFullChatById(chatId);
            await RebuildContextShiftCacheAsync(chatId);

            NotifyStateChanged();

            await GetDiff();
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
            var chat = await context.Chats.Include(c => c.Messages).ThenInclude(m => m.Versions).FirstOrDefaultAsync(c => c.Id == id);
            return chat ?? throw new Exception("Chat not found");
        }
        public async Task<ChatDb> GetFullChatById(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            var chat = await context.Chats
                                .Include(c => c.Messages)
                                    .ThenInclude(m => m.Versions)
                                        .ThenInclude(v => v.Snapshot)
                                            .ThenInclude(s => s.Entries)
                            .FirstOrDefaultAsync(c => c.Id == id);
            return chat ?? throw new Exception("Chat not found");
        }
        private async Task RefreshChatSlot(int chatId, bool isCompare = false)
        {
            var chat = await GetFullChatById(chatId);

            
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

            if (CurrentChatDb != null && CurrentChatDb.Id == chatId)
            {
                CurrentChatDb = chat;
                CurrentChatDbId = chatId;
            }
        }
        public async Task AddMessageAsync(int chatId, string role, string content, string? model, bool isOriginal = false)
        {
            using var context = _contextFactory.CreateDbContext();
            var chat = await context.Chats
                .Include(c => c.Messages).ThenInclude(m => m.Versions)
                .FirstOrDefaultAsync(c => c.Id == chatId);
            if (chat == null) throw new Exception("Chat not found");

            var msg = new MessageDb { Role = role, Order = chat.Messages.Count };

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
                return new();
            }
            if (messageOrder == null)
            {
                messageOrder = chat.Messages.Count - 1;
            }
            List<VersionDb> previousVersions = new();
            for (int i = 0; i < messageOrder; i++)
            {
                var vers = chat.Messages[i].GetCurrentVersion();
                previousVersions.Add(vers);
            }
            return previousVersions;
        }
        public async Task UpdateMessageAsync(int messageId, string newContent, string newModel, bool isOriginal = false, bool isCompare = false)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var message = await context.Messages
                .Include(m => m.Versions)
                .FirstOrDefaultAsync(m => m.Id == messageId);

            if (message == null) throw new Exception("Сообщение не найдено");

            GetCurrentVersionsChat(isCompare);
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

            await context.SaveChangesAsync();

            await RefreshChatSlot(message.ChatId, isCompare);

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
            await UpdateMessageDiffForMessage(message, isCompare);
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


        public async Task SwitchToVersionAsync(int chatId, int messageId, int versionId, bool isCompare = false)
        {
            MessageDb? msg = null;
            if (CompareWithChatId == CurrentChatDbId && CompareWithChatId == chatId)
            {
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
                await UpdateMessageDiffForMessage(msg, isCompare);
            }
            else
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                msg = context.Messages
                            .Include(m => m.Versions)
                            .FirstOrDefault(m => m.Id == messageId);
                if (msg == null)
                {
                    return;
                }

                var vers = msg!.Versions.FirstOrDefault(v => v.Id == versionId);
                if (vers == null)
                {
                    return;
                }

                msg.CurrentVersionOrder = vers.Order;
                await context.SaveChangesAsync();
                await RefreshChatSlot(chatId);
                await UpdateContextShiftAfterEditAsync(chatId, msg.Order, isCompare);
                await RefreshDbChatsAsync();
                await UpdateMessageDiffForMessage(msg, isCompare);
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
                msg.CurrentVersionOrder++;
                await context.SaveChangesAsync();
            }
        }

        public async Task PreviousVersions(int chatId, VersionDb lastVersion, bool isCompare = false)
        {
            ChatDb? chat = isCompare ? CompareWithChat : CurrentChatDb;
            ChatSnapshot? snapshot = lastVersion.Snapshot;

            if (snapshot == null || snapshot.Entries == null)
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                snapshot = await context.ChatSnapshots
                    .Include(s => s.Entries)
                    .FirstOrDefaultAsync(s => s.Id == lastVersion.SnapshotId);
            }

            if (snapshot == null || snapshot.Entries == null)
            {
                return;
            }
            foreach (var entry in snapshot.Entries!)
            {
                await SwitchToVersionAsync(chatId, entry.MessageId, entry.VersionId, isCompare);
            }
            NotifyStateChanged();
        }
        private async Task<ChatDb?> GetFullChat(int chatId, bool isCompare = false)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();

            var chat = await context.Chats
                .Include(c => c.Messages)
                    .ThenInclude(m => m.Versions)
                        .ThenInclude(v => v.Snapshot)
                            .ThenInclude(s => s.Entries)
                .FirstOrDefaultAsync(c => c.Id == chatId);

            if (CurrentChatDbId == CompareWithChatId && CurrentChatDbId == chatId)
            {
                if (isCompare)
                {
                    foreach (var msg in chat.Messages)
                    {
                        var curMsg = CompareWithChat.Messages.FirstOrDefault(m => m.Id == msg.Id);
                        if (curMsg == null) continue;
                        msg.CurrentVersionOrder = curMsg.CurrentVersionOrder;
                    }
                }
                else
                {
                    foreach (var msg in chat.Messages)
                    {
                        var curMsg = CurrentChatDb.Messages.FirstOrDefault(m => m.Id == msg.Id);
                        if (curMsg == null) continue;
                        msg.CurrentVersionOrder = curMsg.CurrentVersionOrder;
                    }
                }
            }

            return chat;
        }
        private async Task UpdateContextShiftAfterEditAsync(int chatId, int editedMsgOrder, bool isCompare = false)
        {
            var chat = await GetFullChat(chatId, isCompare);
            if (chat == null)
            {
                return;
            }
            var lookup = chat.Messages.ToDictionary(m => m.Id);
            foreach (var m in chat.Messages.OrderBy(m => m.Order))
            {
                if (m.Order < editedMsgOrder) continue;
                var curVers = m.GetCurrentVersion();
                if (curVers.Snapshot?.Entries == null)
                {
                    continue; 
                }
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

        public async Task SetCompareWith(int? chatId)
        {
            CompareWithChatId = chatId;

            if (chatId == CurrentChatDbId) CompareWithChat = new ChatDb(CurrentChatDb);
            else CompareWithChat = chatId == null ? null : await GetFullChatById((int)chatId);

            if (chatId != null)
            {
                await RebuildContextShiftCacheAsync(chatId.Value, true);
            }
            else
            {
                ResetCompareAlignment();
            }
            OnComparisonChanged?.Invoke(chatId != null);
            if (IsCompareAligned)
            {
                OnCompareStart.Invoke();
                await GetDiff();
            }
        }
        public async Task CreateBranchAsync(int chatId, int messageId, bool isCompare = false, bool fullCopy = true)
        {
            var chat = await GetFullChatById(chatId);
            using var context = _contextFactory.CreateDbContext();
            var targetMessage = chat.Messages.FirstOrDefault(m => m.Id == messageId);
            if (targetMessage == null) return;

            var newChat = new ChatDb
            {
                Name = $"Chat {DbChats.Count + 1} (ветка чата \"{chat.Name}\")",
                CreatedAt = DateTimeOffset.UtcNow
            };

            var newVersionsLookup = new Dictionary<(int msgOrder, int verOrder), VersionDb>();

            foreach (var mes in chat.Messages.OrderBy(m => m.Order))
            {
                if (mes.Order > targetMessage.Order) continue;

                var newMes = MessageDb.CreateCleanMessage(mes);

                foreach (var v in newMes.Versions)
                {
                    newVersionsLookup[(mes.Order, v.Order)] = v;
                }

                newChat.Messages.Add(newMes);
            }

            foreach (var newMes in newChat.Messages)
            {
                foreach (var newVer in newMes.Versions)
                {
                    var originalMes = chat.Messages.First(m => m.Order == newMes.Order);
                    var originalVer = originalMes.Versions.First(v => v.Order == newVer.Order);

                    if (originalVer.Snapshot != null)
                    {
                        var newSnapshot = new ChatSnapshot
                        {
                            CreatedAt = originalVer.Snapshot.CreatedAt,
                            Entries = new List<SnapshotEntry>(),
                            Chat = newChat
                        };

                        foreach (var originalEntry in originalVer.Snapshot.Entries)
                        {
                            var targetNewVer = newVersionsLookup.GetValueOrDefault((originalEntry.Message.Order, originalEntry.Version.Order));

                            if (targetNewVer != null)
                            {
                                var newEntry = new SnapshotEntry
                                {
                                    Message = targetNewVer.Message,
                                    Version = targetNewVer,
                                    Snapshot = newSnapshot
                                };
                                newSnapshot.Entries.Add(newEntry);
                            }
                        }
                        newVer.Snapshot = newSnapshot;
                    }
                }
            }

            await context.Chats.AddAsync(newChat);
            await context.SaveChangesAsync();

            await RefreshDbChatsAsync();
            NotifyStateChanged();
        }
        private async Task GetDiff()
        {
            messagesDiffs.Clear();
            if (CurrentChatDb == null || CompareWithChat == null ||
                CompareStartOrderCompare == null || CompareStartOrderMain == null)
                return;
            messagesDiffs = await _semanticService.CompareAsync(
                        CurrentChatDb, CompareWithChat, (int)CompareStartOrderMain, (int)CompareStartOrderCompare);

            OnMessagesDiffsChange?.Invoke();
        }
        private async Task UpdateMessageDiffForMessage(MessageDb newMessage, bool isCompare)
        {
            if (messagesDiffs.Count > 0 && IsCompareAligned)
            {
                int targetIndex = newMessage.Order -
                                    (isCompare ? (int)CompareStartOrderCompare! : (int)CompareStartOrderMain!);
                if (messagesDiffs.Count <= targetIndex || targetIndex < 0) return;
                var firstMessage = CurrentChatDb?.Messages.FirstOrDefault(m => m.Order == (targetIndex + CompareStartOrderMain));
                var secondMessage = CompareWithChat?.Messages.FirstOrDefault(m => m.Order == (targetIndex + CompareStartOrderCompare));
                if (firstMessage == null || secondMessage == null)
                    return;

                var temp = await _semanticService.RecalculateSingleMessageAsync(firstMessage.GetCurrentVersion().Content, secondMessage.GetCurrentVersion().Content);

                messagesDiffs[targetIndex] = temp;
                OnMessagesDiffsChange?.Invoke();
            }
        }
    }
}
