using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

public class ChatHub : Hub
{
    // Aktif kullanıcıları tutan yapı (Key: ConnectionId)
    private static ConcurrentDictionary<string, UserConnection> Connections = new ConcurrentDictionary<string, UserConnection>();

    // Basit spam koruması: Her bağlantı için son mesaj zamanı
    private static ConcurrentDictionary<string, DateTime> LastMessageTime = new ConcurrentDictionary<string, DateTime>();

    // Mesaj id'si ve gönderen bağlantı id'si eşleştirmesi
    private static ConcurrentDictionary<string, string> MessageSenderMapping = new ConcurrentDictionary<string, string>();

    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var gender = httpContext.Request.Query["gender"].ToString();
        var username = httpContext.Request.Query["username"].ToString();
        var language = httpContext.Request.Query["language"].ToString();

        var userConnection = new UserConnection
        {
            ConnectionId = Context.ConnectionId,
            Gender = gender,
            Username = username,
            Language = language,
            CurrentChatPartner = string.Empty,
            Age = 0,
            Zodiac = "",
            Height = 0,
            Weight = 0
        };

        Connections[Context.ConnectionId] = userConnection;

        await Clients.Caller.SendAsync("Connected", "Hoş geldiniz!");
        await Pass();
        await UpdateOnlineCount();

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception exception)
    {
        if (Connections.TryRemove(Context.ConnectionId, out var disconnectedUser))
        {
            if (!string.IsNullOrEmpty(disconnectedUser.CurrentChatPartner) &&
                Connections.TryGetValue(disconnectedUser.CurrentChatPartner, out var partner))
            {
                partner.CurrentChatPartner = string.Empty;
                await Clients.Client(partner.ConnectionId).SendAsync("PartnerDisconnected", "Eşleştiğiniz kullanıcı bağlantıyı kapattı.");
            }
        }
        await UpdateOnlineCount();
        await base.OnDisconnectedAsync(exception);
    }

    private Task UpdateOnlineCount() => Clients.All.SendAsync("UpdateOnlineCount", Connections.Count);

    public async Task Pass()
    {
        if (Connections.TryGetValue(Context.ConnectionId, out var currentUser))
        {
            if (!string.IsNullOrEmpty(currentUser.CurrentChatPartner))
            {
                if (Connections.TryGetValue(currentUser.CurrentChatPartner, out var oldPartner))
                {
                    oldPartner.CurrentChatPartner = string.Empty;
                    await Clients.Client(oldPartner.ConnectionId)
                        .SendAsync("PartnerDisconnected", "Eski eşleşmeniz yeni eşleşme talebi nedeniyle kapatıldı.");
                }
                currentUser.CurrentChatPartner = string.Empty;
            }

            var availableUsers = Connections.Values
                .Where(u => u.Gender == currentUser.Gender &&
                            u.ConnectionId != currentUser.ConnectionId &&
                            string.IsNullOrEmpty(u.CurrentChatPartner) &&
                            !BlockListStore.IsBlocked(currentUser.Username, u.Username) &&
                            !BlockListStore.IsBlocked(u.Username, currentUser.Username))
                .ToList();

            if (availableUsers.Any())
            {
                var random = new Random();
                var selectedUser = availableUsers[random.Next(availableUsers.Count)];

                currentUser.CurrentChatPartner = selectedUser.ConnectionId;
                selectedUser.CurrentChatPartner = currentUser.ConnectionId;

                var encryptionKey = Guid.NewGuid().ToString("N");

                await Clients.Client(currentUser.ConnectionId)
                    .SendAsync("Matched", "Eşleşme sağlandı.", encryptionKey);
                await Clients.Client(selectedUser.ConnectionId)
                    .SendAsync("Matched", "Eşleşme sağlandı.", encryptionKey);
            }
            else
            {
                await Clients.Caller.SendAsync("NoMatch", "Şu anda uygun eşleşme bulunamadı. Lütfen tekrar deneyin.");
            }
        }
    }

    public async Task SendMessage(string encryptedMessage)
    {
        if (LastMessageTime.TryGetValue(Context.ConnectionId, out DateTime lastTime))
        {
            if ((DateTime.UtcNow - lastTime).TotalMilliseconds < 500)
            {
                await Clients.Caller.SendAsync("Error", "Çok hızlı mesaj gönderiyorsunuz. Lütfen biraz bekleyin.");
                return;
            }
        }
        LastMessageTime[Context.ConnectionId] = DateTime.UtcNow;

        if (Connections.TryGetValue(Context.ConnectionId, out var currentUser) &&
            !string.IsNullOrEmpty(currentUser.CurrentChatPartner))
        {
            var messageId = Guid.NewGuid().ToString();
            MessageSenderMapping[messageId] = Context.ConnectionId;

            var partnerConnection = currentUser.CurrentChatPartner;
            if (Connections.TryGetValue(partnerConnection, out var partner) &&
                BlockListStore.IsBlocked(partner.Username, currentUser.Username))
            {
                await Clients.Caller.SendAsync("Error", "Karşı taraf sizi engellemiş.");
                return;
            }
            await Clients.Client(partnerConnection).SendAsync("ReceiveMessage", currentUser.Username, encryptedMessage, messageId);
            await Clients.Caller.SendAsync("MessageSent", currentUser.Username, encryptedMessage, messageId);
        }
        else { await Clients.Caller.SendAsync("Error", "Eşleşme bulunamadı."); }
    }

    public async Task MarkMessageAsRead(string messageId)
    {
        if (MessageSenderMapping.TryRemove(messageId, out var senderConnection))
        {
            await Clients.Client(senderConnection).SendAsync("MessageRead", messageId);
        }
    }

    public async Task SendMedia(string mediaType, string mediaContent)
    {
        if (Connections.TryGetValue(Context.ConnectionId, out var currentUser) &&
            !string.IsNullOrEmpty(currentUser.CurrentChatPartner))
        {
            var partnerConnection = currentUser.CurrentChatPartner;
            if (Connections.TryGetValue(partnerConnection, out var partner) &&
                BlockListStore.IsBlocked(partner.Username, currentUser.Username))
            {
                await Clients.Caller.SendAsync("Error", "Karşı taraf sizi engellemiş.");
                return;
            }
            await Clients.Client(partnerConnection).SendAsync("ReceiveMedia", mediaType, mediaContent);
            await Clients.Caller.SendAsync("MediaSent", "Medya gönderildi.");
        }
        else { await Clients.Caller.SendAsync("Error", "Eşleşme bulunamadı."); }
    }

    public async Task BlockUser()
    {
        if (Connections.TryGetValue(Context.ConnectionId, out var currentUser) &&
            !string.IsNullOrEmpty(currentUser.CurrentChatPartner))
        {
            if (Connections.TryGetValue(currentUser.CurrentChatPartner, out var partner))
            {
                BlockListStore.AddBlock(currentUser.Username, partner.Username);
                partner.CurrentChatPartner = string.Empty;
                await Clients.Client(partner.ConnectionId).SendAsync("PartnerBlocked", "Karşı taraf tarafından engellendiniz. Bundan sonra bir daha karşılaşmayacaksınız.");
            }
            currentUser.CurrentChatPartner = string.Empty;
            await Clients.Caller.SendAsync("Blocked", "Kullanıcı engellendi. Bundan sonra bu kullanıcıyla eşleşmeyeceksiniz.");
        }
        else { await Clients.Caller.SendAsync("Error", "Engellenecek eşleşme bulunamadı."); }
    }

    public Task Ping() => Clients.Caller.SendAsync("Pong");

    public Task RegisterProfile(int age, string zodiac, double height, double weight)
    {
        if (Connections.TryGetValue(Context.ConnectionId, out var currentUser))
        {
            currentUser.Age = age;
            currentUser.Zodiac = zodiac;
            currentUser.Height = height;
            currentUser.Weight = weight;
        }
        return Task.CompletedTask;
    }

    public Task<UserProfile> GetProfile(string username)
    {
        var user = Connections.Values.FirstOrDefault(u => u.Username == username);
        if (user != null)
        {
            var profile = new UserProfile
            {
                Username = user.Username,
                Gender = user.Gender,
                Language = user.Language,
                Age = user.Age,
                Zodiac = user.Zodiac,
                Height = user.Height,
                Weight = user.Weight
            };
            return Task.FromResult(profile);
        }
        return Task.FromResult<UserProfile>(null);
    }
}

public class UserConnection
{
    public string ConnectionId { get; set; }
    public string Gender { get; set; }
    public string Username { get; set; }
    public string Language { get; set; }  // Yeni: Dil bilgisi
    public string CurrentChatPartner { get; set; }
    public int Age { get; set; }
    public string Zodiac { get; set; }
    public double Height { get; set; }
    public double Weight { get; set; }
}

public class UserProfile
{
    public string Username { get; set; }
    public string Gender { get; set; }
    public string Language { get; set; }  // Dil bilgisi
    public int Age { get; set; }
    public string Zodiac { get; set; }
    public double Height { get; set; }
    public double Weight { get; set; }
}

public static class BlockListStore
{
    private static ConcurrentDictionary<string, HashSet<string>> _blockedUsers = new ConcurrentDictionary<string, HashSet<string>>();
    public static ConcurrentDictionary<string, HashSet<string>> BlockedUsers => _blockedUsers;
    public static void AddBlock(string blocker, string blocked)
    {
        var list = _blockedUsers.GetOrAdd(blocker, new HashSet<string>());
        lock (list) { list.Add(blocked); }
    }
    public static bool IsBlocked(string blocker, string blocked)
    {
        if (_blockedUsers.TryGetValue(blocker, out var list))
        {
            lock (list) { return list.Contains(blocked); }
        }
        return false;
    }
}
